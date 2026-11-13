--liquibase formatted sql

--changeset hr:time_absence_app.absence_type_list runOnChange:true splitStatements:false
--comment: Słownik rodzajów nieobecności (R6; każdy zalogowany). limit_kind: none, leave (limit urlopu; UZ dodatkowo
--comment: limit_days w roku), yearDays (dni w roku), event (dni na zdarzenie).
DROP FUNCTION IF EXISTS time_absence_app.absence_type_list();
CREATE FUNCTION time_absence_app.absence_type_list()
    RETURNS TABLE (
        code text,
        name text,
        payment text,
        limit_kind text,
        limit_days integer,
        sickness boolean,
        legal_basis text
    )
    LANGUAGE plpgsql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    PERFORM platform.require_role('kadry', 'administrator', 'pracownik');
    RETURN QUERY
    SELECT t.code, t.name, t.payment, t.limit_kind, t.limit_days, t.sickness, t.legal_basis
    FROM time_absence.absence_types t
    ORDER BY t.sort_order;
END;
$$;

--changeset hr:time_absence_app.absence_list runOnChange:true splitStatements:false
--comment: Nieobecności umowy nachodzące na rok (R7, R9; Kadry, Administrator); pusty rok — wszystkie. Dni kalendarzowe,
--comment: dni robocze i godziny pracy (× etat; zlecenie — 0) liczone przy odczycie z kalendarza i warunków.
DROP FUNCTION IF EXISTS time_absence_app.absence_list(uuid, integer);
CREATE FUNCTION time_absence_app.absence_list(p_employment_id uuid, p_year integer)
    RETURNS TABLE (
        id uuid,
        absence_type_id text,
        absence_type_name text,
        date_from date,
        date_to date,
        calendar_days integer,
        work_days integer,
        work_hours numeric,
        certificate_number text,
        note text,
        row_version bigint
    )
    LANGUAGE plpgsql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_employment employment.employments%ROWTYPE;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    v_employment := time_absence.employment_get(p_employment_id, ARRAY['employment', 'mandate', 'specific_work']);
    RETURN QUERY
    SELECT a.id, t.code, t.name, a.date_from, a.date_to, a.date_to - a.date_from + 1,
           CASE WHEN v_employment.kind = 'employment'
               THEN time_absence.work_days(a.employment_id, a.date_from, a.date_to) ELSE 0 END,
           CASE WHEN v_employment.kind = 'employment'
               THEN time_absence.working_hours(a.employment_id, a.date_from, a.date_to) ELSE 0 END,
           a.certificate_number, a.note, a.xmin::text::bigint
    FROM time_absence.absences a
    JOIN time_absence.absence_types t ON t.code = a.absence_type_code
    WHERE a.employment_id = p_employment_id
      AND (p_year IS NULL OR daterange(a.date_from, a.date_to, '[]')
          && daterange(make_date(p_year, 1, 1), make_date(p_year, 12, 31), '[]'))
    ORDER BY a.date_from;
END;
$$;

--changeset hr:time_absence.absence_check runOnChange:true splitStatements:false
--comment: Walidacja zapisu nieobecności (R7–R11): błędy pól, kalendarz spółki (umowa o pracę — U0003 calendarMissing),
--comment: limity. p_id = NULL przy nowej nieobecności.
CREATE OR REPLACE FUNCTION time_absence.absence_check(
    p_id uuid,
    p_employment employment.employments,
    p_type text,
    p_from date,
    p_to date,
    p_certificate text,
    p_note text
)
    RETURNS void
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_errors jsonb;
BEGIN
    v_errors := time_absence.absence_validate(p_id, p_employment.id, p_type, p_from, p_to, p_certificate, p_note);
    IF v_errors <> '{}' THEN
        PERFORM platform.fail_validation(v_errors);
    END IF;
    IF p_employment.kind = 'employment' THEN
        IF time_absence.calendar_missing(p_employment.company_id, p_from, p_to) THEN
            RAISE EXCEPTION 'company calendar missing' USING ERRCODE = 'U0003', HINT = 'calendarMissing';
        END IF;
        v_errors := time_absence.absence_limit_errors(p_id, p_employment.id, p_type, p_from, p_to);
        IF v_errors <> '{}' THEN
            PERFORM platform.fail_validation(v_errors);
        END IF;
    END IF;
END;
$$;

--changeset hr:time_absence_app.absence_create runOnChange:true splitStatements:false
--comment: Nowa nieobecność umowy (R7–R11). Numer e-ZLA wymagany przy chorobie. Okres zamkniętej listy płac — U0003
--comment: payrollClosed, naliczonej — lista nieaktualna (009 R2, R25).
CREATE OR REPLACE PROCEDURE time_absence_app.absence_create(
    IN p_employment_id uuid,
    IN p_absence_type_id text,
    IN p_date_from date,
    IN p_date_to date,
    IN p_certificate_number text,
    IN p_note text,
    INOUT p_id uuid
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_certificate text := nullif(btrim(p_certificate_number), '');
    v_note text := nullif(btrim(p_note), '');
    v_employment employment.employments%ROWTYPE;
    v_row time_absence.absences%ROWTYPE;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    -- Blokada umowy szereguje sprawdzanie limitów i nakładania.
    PERFORM FROM employment.employments m WHERE m.id = p_employment_id FOR UPDATE;
    v_employment := time_absence.employment_get(p_employment_id, ARRAY['employment', 'mandate', 'specific_work']);
    PERFORM time_absence.absence_check(NULL, v_employment, p_absence_type_id, p_date_from, p_date_to, v_certificate,
        v_note);
    PERFORM payroll.guard(v_employment.employee_id, p_employment_id, p_date_from, p_date_to);

    INSERT INTO time_absence.absences (employment_id, absence_type_code, date_from, date_to, certificate_number,
                                       note, created_by)
    VALUES (p_employment_id, p_absence_type_id, p_date_from, p_date_to, v_certificate, v_note, platform.actor_id())
    RETURNING * INTO v_row;
    p_id := v_row.id;
    PERFORM platform.audit('absence', p_id::text, 'create', NULL, to_jsonb(v_row));
END;
$$;

--changeset hr:time_absence_app.absence_update runOnChange:true splitStatements:false
--comment: Zmiana nieobecności (R12), z wersją wiersza. Stary i nowy okres w okresie zamkniętej listy płac — U0003
--comment: payrollClosed, naliczonej — lista nieaktualna (009 R2, R25).
CREATE OR REPLACE PROCEDURE time_absence_app.absence_update(
    IN p_id uuid,
    IN p_row_version bigint,
    IN p_absence_type_id text,
    IN p_date_from date,
    IN p_date_to date,
    IN p_certificate_number text,
    IN p_note text
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_certificate text := nullif(btrim(p_certificate_number), '');
    v_note text := nullif(btrim(p_note), '');
    v_employment employment.employments%ROWTYPE;
    v_before time_absence.absences%ROWTYPE;
    v_after time_absence.absences%ROWTYPE;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    SELECT * INTO v_before FROM time_absence.absences a WHERE a.id = p_id;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'absence not found' USING ERRCODE = 'U0002';
    END IF;
    PERFORM FROM employment.employments m WHERE m.id = v_before.employment_id FOR UPDATE;
    PERFORM FROM time_absence.absences a WHERE a.id = p_id FOR UPDATE;
    IF (SELECT a.xmin::text::bigint FROM time_absence.absences a WHERE a.id = p_id) <> p_row_version THEN
        RAISE EXCEPTION 'absence changed' USING ERRCODE = 'U0003';
    END IF;
    v_employment := time_absence.employment_get(v_before.employment_id,
        ARRAY['employment', 'mandate', 'specific_work']);
    PERFORM time_absence.absence_check(p_id, v_employment, p_absence_type_id, p_date_from, p_date_to, v_certificate,
        v_note);
    PERFORM payroll.guard(v_employment.employee_id, v_employment.id, v_before.date_from, v_before.date_to);
    PERFORM payroll.guard(v_employment.employee_id, v_employment.id, p_date_from, p_date_to);

    UPDATE time_absence.absences a
    SET absence_type_code = p_absence_type_id, date_from = p_date_from, date_to = p_date_to,
        certificate_number = v_certificate, note = v_note
    WHERE a.id = p_id
    RETURNING * INTO v_after;
    PERFORM platform.audit('absence', p_id::text, 'update', to_jsonb(v_before), to_jsonb(v_after));
END;
$$;

--changeset hr:time_absence_app.absence_delete runOnChange:true splitStatements:false
--comment: Usunięcie nieobecności (R12), z wersją wiersza. Blokada i nieaktualność list jak przy zmianie (009 R25).
CREATE OR REPLACE PROCEDURE time_absence_app.absence_delete(IN p_id uuid, IN p_row_version bigint)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_row time_absence.absences%ROWTYPE;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    SELECT * INTO v_row FROM time_absence.absences a WHERE a.id = p_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'absence not found' USING ERRCODE = 'U0002';
    END IF;
    IF (SELECT a.xmin::text::bigint FROM time_absence.absences a WHERE a.id = p_id) <> p_row_version THEN
        RAISE EXCEPTION 'absence changed' USING ERRCODE = 'U0003';
    END IF;
    PERFORM payroll.guard(NULL, v_row.employment_id, v_row.date_from, v_row.date_to);
    DELETE FROM time_absence.absences a WHERE a.id = p_id;
    PERFORM platform.audit('absence', p_id::text, 'delete', to_jsonb(v_row), NULL);
END;
$$;

--changeset hr:time_absence_app.my_absence_list runOnChange:true splitStatements:false
--comment: Nieobecności kartoteki w roku ze wszystkich umów (R15, R19). Pracownik — tylko swoja kartoteka.
DROP FUNCTION IF EXISTS time_absence_app.my_absence_list(uuid, integer);
CREATE FUNCTION time_absence_app.my_absence_list(p_employee_id uuid, p_year integer)
    RETURNS TABLE (
        id uuid,
        employment_id uuid,
        employment_number text,
        absence_type_id text,
        absence_type_name text,
        date_from date,
        date_to date,
        calendar_days integer,
        work_hours numeric
    )
    LANGUAGE plpgsql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    PERFORM time_absence.require_employee_access(p_employee_id);
    RETURN QUERY
    SELECT a.id, m.id, m.number, t.code, t.name, a.date_from, a.date_to, a.date_to - a.date_from + 1,
           CASE WHEN m.kind = 'employment'
               THEN time_absence.working_hours(a.employment_id, a.date_from, a.date_to) ELSE 0 END
    FROM time_absence.absences a
    JOIN employment.employments m ON m.id = a.employment_id
    JOIN time_absence.absence_types t ON t.code = a.absence_type_code
    WHERE m.employee_id = p_employee_id
      AND daterange(a.date_from, a.date_to, '[]') && daterange(make_date(p_year, 1, 1), make_date(p_year, 12, 31), '[]')
    ORDER BY a.date_from;
END;
$$;
