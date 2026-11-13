--liquibase formatted sql

--changeset hr:ppk_app.enrollment_candidates runOnChange:true splitStatements:false
--comment: Lista „do zapisu” spółki na dzień (R3–R5; Kadry, Administrator): osoby zatrudnione w dniu, 18–69 lat, bez
--comment: uczestnictwa i bez rezygnacji. status: waiting (3 miesiące upłyną reached_on), toEnroll (termin deadline
--comment: przed nami), overdue (termin minął — umowa z mocy prawa, art. 17), requestOnly (55–69 bez wniosku
--comment: o zawarcie umowy). Osoby, którym 3 miesiące nie upłyną w ciągu 92 dni (koniec umowy), są pomijane.
DROP FUNCTION IF EXISTS ppk_app.enrollment_candidates(uuid, date);
CREATE FUNCTION ppk_app.enrollment_candidates(p_company_id uuid, p_on date)
    RETURNS TABLE (
        employee_id uuid,
        employee_number text,
        first_name text,
        last_name text,
        birth_date date,
        age integer,
        reached_on date,
        deadline date,
        status text,
        enrollment_request boolean
    )
    LANGUAGE plpgsql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_on date := coalesce(p_on, current_date);
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    IF NOT EXISTS (SELECT FROM platform.companies c WHERE c.id = p_company_id) THEN
        RAISE EXCEPTION 'company not found' USING ERRCODE = 'U0002';
    END IF;

    RETURN QUERY
    SELECT y.id, y.number, y.first_name, y.last_name, y.birth_date, y.age, y.reached_on,
           ppk.enrollment_deadline(y.reached_on),
           CASE
               WHEN y.reached_on > v_on THEN 'waiting'
               WHEN y.age >= 55 AND NOT y.request THEN 'requestOnly'
               WHEN ppk.enrollment_deadline(y.reached_on) < v_on THEN 'overdue'
               ELSE 'toEnroll'
           END,
           y.request
    FROM (
        SELECT x.*, ppk.three_months_reached_on(x.id, v_on) AS reached_on
        FROM (
            SELECT e.id, e.number, e.first_name, e.last_name, e.birth_date,
                   ppk.age_on(e.birth_date, v_on) AS age,
                   EXISTS (SELECT FROM ppk.declarations d
                           WHERE d.employee_id = e.id AND d.kind = 'enrollmentRequest' AND d.submitted_at <= v_on)
                       AS request
            FROM employees.employees e
            WHERE e.company_id = p_company_id
              AND NOT EXISTS (SELECT FROM ppk.participations p WHERE p.employee_id = e.id)
              AND ppk.status_on(e.id, v_on) <> 'resigned'
              AND ppk.is_employed_person(e.id, v_on)
        ) x
        WHERE x.age BETWEEN 18 AND 69
    ) y
    WHERE y.reached_on IS NOT NULL
    ORDER BY y.reached_on, lower(y.last_name), lower(y.first_name), y.number;
END;
$$;

--changeset hr:ppk_app.participation_get runOnChange:true splitStatements:false
--comment: PPK kartoteki dzisiaj (R6, R11, R14): uczestnictwo (enrolled = false, gdy brak), status, stawki w procentach
--comment: obowiązujące dziś (zera, gdy status inny niż active), czy jest osobą zatrudnioną, wiek, wersja wiersza
--comment: uczestnictwa (do usunięcia pomyłki; pusta bez uczestnictwa). Kadry i Administrator
--comment: — każda kartoteka; Pracownik — tylko swoja.
DROP FUNCTION IF EXISTS ppk_app.participation_get(uuid);
CREATE FUNCTION ppk_app.participation_get(p_employee_id uuid)
    RETURNS TABLE (
        employee_id uuid,
        enrolled boolean,
        enrolled_at date,
        institution_member_number text,
        status text,
        employee_basic_rate numeric,
        employee_additional_rate numeric,
        employer_basic_rate numeric,
        employer_additional_rate numeric,
        employed_person boolean,
        age integer,
        row_version bigint
    )
    LANGUAGE plpgsql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    PERFORM ppk.require_employee_access(p_employee_id);
    RETURN QUERY
    SELECT e.id, p.id IS NOT NULL, p.enrolled_at, p.institution_member_number,
           ppk.status_on(e.id, current_date),
           r.employee_basic_rate, r.employee_additional_rate, r.employer_basic_rate, r.employer_additional_rate,
           ppk.is_employed_person(e.id, current_date),
           ppk.age_on(e.birth_date, current_date),
           p.xmin::text::bigint
    FROM employees.employees e
    LEFT JOIN ppk.participations p ON p.employee_id = e.id
    CROSS JOIN LATERAL ppk.rates_on(e.id, current_date) r
    WHERE e.id = p_employee_id;
END;
$$;

--changeset hr:ppk_app.rate_period_list runOnChange:true splitStatements:false
--comment: Historia stawek pracownika w procentach, od najnowszej (R6, R10, R14). Dostęp jak participation_get.
DROP FUNCTION IF EXISTS ppk_app.rate_period_list(uuid);
CREATE FUNCTION ppk_app.rate_period_list(p_employee_id uuid)
    RETURNS TABLE (
        valid_from date,
        valid_to date,
        employee_basic_rate numeric,
        employee_additional_rate numeric
    )
    LANGUAGE plpgsql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    PERFORM ppk.require_employee_access(p_employee_id);
    RETURN QUERY
    SELECT r.valid_from, r.valid_to, r.employee_basic_rate, r.employee_additional_rate
    FROM ppk.rate_periods r
    JOIN ppk.participations p ON p.id = r.participation_id
    WHERE p.employee_id = p_employee_id
    ORDER BY r.valid_from DESC;
END;
$$;

--changeset hr:ppk_app.participation_create runOnChange:true splitStatements:false
--comment: Zapis do PPK — data zawarcia umowy o prowadzenie PPK (R6, R7). enrolledAt: notEmployed (nie jest osobą
--comment: zatrudnioną w tym dniu), tooEarly (mniej niż 14 dni zatrudnienia przed zapisem, art. 16 ust. 1), tooYoung
--comment: (poniżej 18 lat), tooOld (70+), requestMissing (55–69 bez wniosku złożonego najpóźniej w dniu zapisu).
--comment: Stawki w procentach: podstawowa 0,5–2, dodatkowa 0–2 (pusta = 0). Drugi zapis — U0003 alreadyEnrolled.
CREATE OR REPLACE PROCEDURE ppk_app.participation_create(
    IN p_employee_id uuid,
    IN p_enrolled_at date,
    IN p_institution_member_number text,
    IN p_employee_basic_rate numeric,
    IN p_employee_additional_rate numeric,
    INOUT p_id uuid
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_member text := nullif(btrim(p_institution_member_number), '');
    v_additional numeric := coalesce(p_employee_additional_rate, 0);
    v_employee employees.employees%ROWTYPE;
    v_age integer;
    v_row ppk.participations%ROWTYPE;
    v_errors jsonb := '{}';
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    SELECT * INTO v_employee FROM employees.employees e WHERE e.id = p_employee_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'employee not found' USING ERRCODE = 'U0002';
    END IF;
    IF EXISTS (SELECT FROM ppk.participations p WHERE p.employee_id = p_employee_id) THEN
        RAISE EXCEPTION 'already enrolled' USING ERRCODE = 'U0003', HINT = 'alreadyEnrolled';
    END IF;

    IF p_enrolled_at IS NULL THEN
        v_errors := v_errors || '{"enrolledAt": ["required"]}';
    ELSE
        v_age := ppk.age_on(v_employee.birth_date, p_enrolled_at);
        IF v_age < 18 THEN
            v_errors := v_errors || '{"enrolledAt": ["tooYoung"]}';
        ELSIF v_age >= 70 THEN
            v_errors := v_errors || '{"enrolledAt": ["tooOld"]}';
        ELSIF NOT ppk.is_employed_person(p_employee_id, p_enrolled_at) THEN
            v_errors := v_errors || '{"enrolledAt": ["notEmployed"]}';
        ELSIF (SELECT count(*) FROM ppk.employed_days(p_employee_id, (p_enrolled_at - interval '1 year')::date + 1,
                                                       p_enrolled_at - 1)) < 14 THEN
            v_errors := v_errors || '{"enrolledAt": ["tooEarly"]}';
        ELSIF v_age >= 55 AND NOT EXISTS (
            SELECT FROM ppk.declarations d
            WHERE d.employee_id = p_employee_id AND d.kind = 'enrollmentRequest' AND d.submitted_at <= p_enrolled_at
        ) THEN
            v_errors := v_errors || '{"enrolledAt": ["requestMissing"]}';
        END IF;
    END IF;
    IF char_length(v_member) > 40 THEN
        v_errors := v_errors || '{"institutionMemberNumber": ["maxLength"]}';
    END IF;
    IF p_employee_basic_rate IS NULL THEN
        v_errors := v_errors || '{"employeeBasicRate": ["required"]}';
    ELSIF p_employee_basic_rate NOT BETWEEN 0.5 AND 2 OR p_employee_basic_rate <> round(p_employee_basic_rate, 2) THEN
        v_errors := v_errors || '{"employeeBasicRate": ["invalid"]}';
    END IF;
    IF v_additional NOT BETWEEN 0 AND 2 OR v_additional <> round(v_additional, 2) THEN
        v_errors := v_errors || '{"employeeAdditionalRate": ["invalid"]}';
    END IF;
    IF v_errors <> '{}' THEN
        PERFORM platform.fail_validation(v_errors);
    END IF;

    INSERT INTO ppk.participations (employee_id, enrolled_at, institution_member_number, initial_basic_rate,
                                    initial_additional_rate, created_by)
    VALUES (p_employee_id, p_enrolled_at, v_member, p_employee_basic_rate, v_additional, platform.actor_id())
    RETURNING * INTO v_row;
    p_id := v_row.id;
    PERFORM ppk.rebuild_rates(p_employee_id);
    PERFORM platform.audit('ppk_participation', p_id::text, 'create', NULL, to_jsonb(v_row));
END;
$$;

--changeset hr:ppk_app.participation_delete runOnChange:true splitStatements:false
--comment: Usunięcie uczestnictwa wpisanego przez pomyłkę (R6a), z wersją wiersza (U0003 przy nieaktualnej): znikają
--comment: uczestnictwo i jego okresy stawek, deklaracje kartoteki zostają. Wpłaty PPK na zamkniętej liście płac — U0003
--comment: payrollClosed (009 R25).
CREATE OR REPLACE PROCEDURE ppk_app.participation_delete(IN p_employee_id uuid, IN p_row_version bigint)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_row ppk.participations%ROWTYPE;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    PERFORM FROM employees.employees e WHERE e.id = p_employee_id FOR UPDATE;
    SELECT * INTO v_row FROM ppk.participations p WHERE p.employee_id = p_employee_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'participation not found' USING ERRCODE = 'U0002';
    END IF;
    IF (SELECT p.xmin::text::bigint FROM ppk.participations p WHERE p.id = v_row.id) IS DISTINCT FROM p_row_version THEN
        RAISE EXCEPTION 'participation changed' USING ERRCODE = 'U0003';
    END IF;
    PERFORM payroll.guard_used('ppk', p_employee_id);
    DELETE FROM ppk.participations p WHERE p.id = v_row.id;
    PERFORM platform.audit('ppk_participation', v_row.id::text, 'delete', to_jsonb(v_row), NULL);
END;
$$;
