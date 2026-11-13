--liquibase formatted sql

--changeset hr:time_absence_app.leave_entitlement_get runOnChange:true splitStatements:false
--comment: Limit urlopu umowy o pracę na rok (R13–R15; Kadry, Administrator): propozycja z KP (p_base_days
--comment: i p_first_job, gdy podane, inaczej zapisane, domyślnie 20 dni), zapisany limit i zaległy (wyliczone albo
--comment: poprawione), wykorzystany (UW, UZ), saldo w godzinach i dniach (÷ 8). carried_over_warning — po 30.09 roku
--comment: zostało niewykorzystanego zaległego urlopu (KP art. 168).
DROP FUNCTION IF EXISTS time_absence_app.leave_entitlement_get(uuid, integer, integer, boolean);
CREATE FUNCTION time_absence_app.leave_entitlement_get(
    p_employment_id uuid,
    p_year integer,
    p_base_days integer,
    p_first_job boolean
)
    RETURNS TABLE (
        saved boolean,
        base_days integer,
        first_job boolean,
        proposed_hours numeric,
        entitled_hours numeric,
        entitled_overridden boolean,
        carried_over_hours numeric,
        carried_over_overridden boolean,
        override_reason text,
        used_hours numeric,
        balance_hours numeric,
        balance_days numeric,
        carried_over_warning boolean,
        row_version bigint
    )
    LANGUAGE plpgsql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_row time_absence.leave_entitlements%ROWTYPE;
    v_entitled numeric;
    v_carried numeric;
    v_used numeric;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    PERFORM time_absence.employment_get(p_employment_id, ARRAY['employment']);
    IF p_year IS NULL OR p_year NOT BETWEEN 2000 AND 2100 THEN
        PERFORM platform.fail_validation('{"year": ["invalid"]}');
    END IF;
    IF p_base_days NOT IN (20, 26) THEN
        PERFORM platform.fail_validation('{"baseDays": ["invalid"]}');
    END IF;
    SELECT * INTO v_row FROM time_absence.leave_entitlements e
    WHERE e.employment_id = p_employment_id AND e.year = p_year;
    v_entitled := time_absence.leave_entitled(p_employment_id, p_year);
    v_carried := time_absence.leave_carried(p_employment_id, p_year);
    v_used := time_absence.leave_used(p_employment_id, p_year);

    RETURN QUERY
    SELECT v_row.id IS NOT NULL,
           v_row.base_days,
           v_row.first_job,
           time_absence.leave_proposal(p_employment_id, p_year, coalesce(p_base_days, v_row.base_days, 20),
               coalesce(p_first_job, v_row.first_job, false)),
           v_entitled,
           v_row.entitled_hours IS NOT NULL,
           v_carried,
           v_row.carried_over_hours IS NOT NULL,
           v_row.override_reason,
           v_used,
           v_entitled + v_carried - v_used,
           round((v_entitled + v_carried - v_used) / 8, 2),
           current_date > make_date(p_year, 9, 30)
               AND v_carried > time_absence.leave_used(p_employment_id, p_year, NULL, make_date(p_year, 9, 30)),
           (SELECT e.xmin::text::bigint FROM time_absence.leave_entitlements e WHERE e.id = v_row.id);
END;
$$;

--changeset hr:time_absence_app.leave_entitlement_set runOnChange:true splitStatements:false
--comment: Zapis limitu urlopu na rok (R13, R14): wymiar 20 albo 26 dni, pierwsza praca, opcjonalnie poprawiony limit
--comment: i zaległy w godzinach (pusty = wyliczany) z uzasadnieniem. Nowy limit — p_row_version pusty; zmiana —
--comment: wersja wiersza.
CREATE OR REPLACE PROCEDURE time_absence_app.leave_entitlement_set(
    IN p_employment_id uuid,
    IN p_year integer,
    IN p_base_days integer,
    IN p_first_job boolean,
    IN p_entitled_hours numeric,
    IN p_carried_over_hours numeric,
    IN p_override_reason text,
    IN p_row_version bigint
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_reason text := nullif(btrim(p_override_reason), '');
    v_employment employment.employments%ROWTYPE;
    v_before time_absence.leave_entitlements%ROWTYPE;
    v_after time_absence.leave_entitlements%ROWTYPE;
    v_errors jsonb := '{}';
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    PERFORM FROM employment.employments m WHERE m.id = p_employment_id FOR UPDATE;
    v_employment := time_absence.employment_get(p_employment_id, ARRAY['employment']);

    IF p_year IS NULL OR p_year NOT BETWEEN 2000 AND 2100 THEN
        v_errors := v_errors || '{"year": ["invalid"]}';
    ELSIF NOT employment.period(v_employment.start_date, v_employment.end_date, v_employment.terminated_at)
        && daterange(make_date(p_year, 1, 1), make_date(p_year, 12, 31), '[]') THEN
        v_errors := v_errors || '{"year": ["outsidePeriod"]}';
    END IF;
    IF p_base_days IS NULL THEN
        v_errors := v_errors || '{"baseDays": ["required"]}';
    ELSIF p_base_days NOT IN (20, 26) THEN
        v_errors := v_errors || '{"baseDays": ["invalid"]}';
    END IF;
    IF p_entitled_hours < 0 OR p_entitled_hours > 9999 OR p_entitled_hours <> round(p_entitled_hours, 2) THEN
        v_errors := v_errors || '{"entitledHours": ["invalid"]}';
    END IF;
    IF p_carried_over_hours < 0 OR p_carried_over_hours > 9999
        OR p_carried_over_hours <> round(p_carried_over_hours, 2) THEN
        v_errors := v_errors || '{"carriedOverHours": ["invalid"]}';
    END IF;
    IF p_entitled_hours IS NULL AND p_carried_over_hours IS NULL THEN
        v_reason := NULL;
    ELSIF v_reason IS NULL THEN
        v_errors := v_errors || '{"overrideReason": ["required"]}';
    ELSIF char_length(v_reason) > 500 THEN
        v_errors := v_errors || '{"overrideReason": ["maxLength"]}';
    END IF;
    IF v_errors <> '{}' THEN
        PERFORM platform.fail_validation(v_errors);
    END IF;

    SELECT * INTO v_before FROM time_absence.leave_entitlements e
    WHERE e.employment_id = p_employment_id AND e.year = p_year FOR UPDATE;
    IF FOUND THEN
        IF p_row_version IS NULL OR (
            SELECT e.xmin::text::bigint FROM time_absence.leave_entitlements e WHERE e.id = v_before.id
        ) <> p_row_version THEN
            RAISE EXCEPTION 'leave entitlement changed' USING ERRCODE = 'U0003';
        END IF;
        UPDATE time_absence.leave_entitlements e
        SET base_days = p_base_days, first_job = coalesce(p_first_job, false), entitled_hours = p_entitled_hours,
            carried_over_hours = p_carried_over_hours, override_reason = v_reason
        WHERE e.id = v_before.id
        RETURNING * INTO v_after;
        PERFORM platform.audit('leave_entitlement', v_after.id::text, 'update', to_jsonb(v_before),
            to_jsonb(v_after));
    ELSE
        INSERT INTO time_absence.leave_entitlements (employment_id, year, base_days, first_job, entitled_hours,
                                                     carried_over_hours, override_reason, created_by)
        VALUES (p_employment_id, p_year, p_base_days, coalesce(p_first_job, false), p_entitled_hours,
                p_carried_over_hours, v_reason, platform.actor_id())
        RETURNING * INTO v_after;
        PERFORM platform.audit('leave_entitlement', v_after.id::text, 'create', NULL, to_jsonb(v_after));
    END IF;
END;
$$;

--changeset hr:time_absence_app.my_leave_balance runOnChange:true splitStatements:false
--comment: Saldo urlopu kartoteki w roku dla każdej umowy o pracę (R15). Pracownik — tylko swoja kartoteka.
DROP FUNCTION IF EXISTS time_absence_app.my_leave_balance(uuid, integer);
CREATE FUNCTION time_absence_app.my_leave_balance(p_employee_id uuid, p_year integer)
    RETURNS TABLE (
        employment_id uuid,
        employment_number text,
        entitled_hours numeric,
        carried_over_hours numeric,
        used_hours numeric,
        balance_hours numeric,
        balance_days numeric
    )
    LANGUAGE plpgsql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    PERFORM time_absence.require_employee_access(p_employee_id);
    RETURN QUERY
    SELECT x.id, x.number, x.entitled, x.carried, x.used, x.entitled + x.carried - x.used,
           round((x.entitled + x.carried - x.used) / 8, 2)
    FROM (
        SELECT m.id, m.number, m.start_date,
               time_absence.leave_entitled(m.id, p_year) AS entitled,
               time_absence.leave_carried(m.id, p_year) AS carried,
               time_absence.leave_used(m.id, p_year) AS used
        FROM employment.employments m
        WHERE m.employee_id = p_employee_id AND m.kind = 'employment'
          AND employment.period(m.start_date, m.end_date, m.terminated_at)
              && daterange(make_date(p_year, 1, 1), make_date(p_year, 12, 31), '[]')
    ) x
    ORDER BY x.start_date;
END;
$$;

--changeset hr:time_absence_app.employment_norm runOnChange:true splitStatements:false
--comment: Wymiar czasu pracy umowy o pracę w miesiącach roku (R5): godziny dni roboczych kalendarza spółki w okresie
--comment: umowy × etat z dnia. p_month pusty — wszystkie miesiące. Rok bez kalendarza — U0003 calendarMissing.
--comment: missing_designated — w miesiącu brak dnia wolnego za święto w sobotę (wymiar o 8 h za wysoki, R3).
DROP FUNCTION IF EXISTS time_absence_app.employment_norm(uuid, integer, integer);
CREATE FUNCTION time_absence_app.employment_norm(p_employment_id uuid, p_year integer, p_month integer)
    RETURNS TABLE (
        month integer,
        calendar_hours numeric,
        work_days integer,
        norm_hours numeric,
        missing_designated boolean
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
    v_employment := time_absence.employment_get(p_employment_id, ARRAY['employment']);
    IF p_year IS NULL OR p_year NOT BETWEEN 2000 AND 2100 THEN
        PERFORM platform.fail_validation('{"year": ["invalid"]}');
    END IF;
    IF p_month NOT BETWEEN 1 AND 12 THEN
        PERFORM platform.fail_validation('{"month": ["invalid"]}');
    END IF;
    IF time_absence.calendar_missing(v_employment.company_id, make_date(p_year, 1, 1), make_date(p_year, 1, 1)) THEN
        RAISE EXCEPTION 'company calendar missing' USING ERRCODE = 'U0003', HINT = 'calendarMissing';
    END IF;

    RETURN QUERY
    SELECT s.month, s.hours,
           time_absence.work_days(p_employment_id, make_date(p_year, s.month, 1),
               (make_date(p_year, s.month, 1) + interval '1 month')::date - 1),
           time_absence.working_hours(p_employment_id, make_date(p_year, s.month, 1),
               (make_date(p_year, s.month, 1) + interval '1 month')::date - 1),
           s.designated_off < s.saturday_holidays
    FROM time_absence.month_summary(v_employment.company_id, p_year) s
    WHERE p_month IS NULL OR s.month = p_month
    ORDER BY s.month;
END;
$$;
