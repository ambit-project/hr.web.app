--liquibase formatted sql

--changeset hr:time_absence_app.holiday_list runOnChange:true splitStatements:false
--comment: Święta ustawowe roku (R1; Kadry, Administrator). is_saturday — święto w sobotę obniża wymiar (R3).
DROP FUNCTION IF EXISTS time_absence_app.holiday_list(integer);
CREATE FUNCTION time_absence_app.holiday_list(p_year integer)
    RETURNS TABLE (date date, name text, is_saturday boolean)
    LANGUAGE plpgsql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    RETURN QUERY
    SELECT h.date, h.name, extract(isodow FROM h.date) = 6
    FROM time_absence.holidays h
    WHERE extract(year FROM h.date) = p_year
    ORDER BY h.date;
END;
$$;

--changeset hr:time_absence_app.holiday_add runOnChange:true splitStatements:false
--comment: Nowe święto (R1; Administrator dopisuje kolejne lata). Kalendarze już wygenerowane się nie zmieniają.
CREATE OR REPLACE PROCEDURE time_absence_app.holiday_add(IN p_date date, IN p_name text)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_name text := nullif(btrim(p_name), '');
    v_errors jsonb := '{}';
    v_row time_absence.holidays%ROWTYPE;
BEGIN
    PERFORM platform.require_role('administrator');
    IF p_date IS NULL THEN
        v_errors := v_errors || '{"date": ["required"]}';
    ELSIF EXISTS (SELECT FROM time_absence.holidays h WHERE h.date = p_date) THEN
        v_errors := v_errors || '{"date": ["duplicate"]}';
    END IF;
    IF v_name IS NULL THEN
        v_errors := v_errors || '{"name": ["required"]}';
    ELSIF char_length(v_name) > 100 THEN
        v_errors := v_errors || '{"name": ["maxLength"]}';
    END IF;
    IF v_errors <> '{}' THEN
        PERFORM platform.fail_validation(v_errors);
    END IF;

    INSERT INTO time_absence.holidays (date, name, created_by)
    VALUES (p_date, v_name, platform.actor_id())
    RETURNING * INTO v_row;
    PERFORM platform.audit('holiday', p_date::text, 'create', NULL, to_jsonb(v_row));
END;
$$;

--changeset hr:time_absence_app.holiday_delete runOnChange:true splitStatements:false
--comment: Usunięcie święta wpisanego przez pomyłkę (R1; Administrator).
CREATE OR REPLACE PROCEDURE time_absence_app.holiday_delete(IN p_date date)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_row time_absence.holidays%ROWTYPE;
BEGIN
    PERFORM platform.require_role('administrator');
    DELETE FROM time_absence.holidays h WHERE h.date = p_date RETURNING * INTO v_row;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'holiday not found' USING ERRCODE = 'U0002';
    END IF;
    PERFORM platform.audit('holiday', p_date::text, 'delete', to_jsonb(v_row), NULL);
END;
$$;

--changeset hr:time_absence_app.holiday_fill_statutory runOnChange:true splitStatements:false
--comment: Uzupełnienie słownika świętami ustawowymi roku (Administrator; przycisk „Pobierz święta ustawowe”). Dopisuje
--comment: tylko brakujące daty — wpisy istniejące (także ręczne) zostają bez zmian; każde dodanie w audycie. Zwraca
--comment: liczbę dodanych. Rok spoza 2000–2100 — year invalid; przed 2025 — U0003 statutoryHolidaysUnsupported.
CREATE OR REPLACE PROCEDURE time_absence_app.holiday_fill_statutory(IN p_year integer, INOUT p_added integer)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_row time_absence.holidays%ROWTYPE;
BEGIN
    PERFORM platform.require_role('administrator');
    IF p_year IS NULL OR p_year NOT BETWEEN 2000 AND 2100 THEN
        PERFORM platform.fail_validation('{"year": ["invalid"]}');
    END IF;

    p_added := 0;
    FOR v_row IN
        INSERT INTO time_absence.holidays (date, name, created_by)
        SELECT s.date, s.name, platform.actor_id()
        FROM time_absence.statutory_holidays(p_year) s
        ORDER BY s.date
        ON CONFLICT (date) DO NOTHING
        RETURNING *
    LOOP
        PERFORM platform.audit('holiday', v_row.date::text, 'create', NULL, to_jsonb(v_row));
        p_added := p_added + 1;
    END LOOP;
END;
$$;

--changeset hr:time_absence_app.calendar_generate runOnChange:true splitStatements:false
--comment: Kalendarz spółki na rok (R2, R3): pn–pt 8 h, sobota i niedziela wolne, święto 0 h. Zwraca liczbę świąt
--comment: w sobotę, za które Kadry wyznaczają dzień wolny. Rok z kalendarzem — U0003 calendarExists; rok bez świąt
--comment: w słowniku — U0003 holidaysMissing.
CREATE OR REPLACE PROCEDURE time_absence_app.calendar_generate(
    IN p_company_id uuid,
    IN p_year integer,
    INOUT p_saturday_holidays integer
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    PERFORM FROM platform.companies c WHERE c.id = p_company_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'company not found' USING ERRCODE = 'U0002';
    END IF;
    IF p_year IS NULL OR p_year NOT BETWEEN 2000 AND 2100 THEN
        PERFORM platform.fail_validation('{"year": ["invalid"]}');
    END IF;
    IF EXISTS (
        SELECT FROM time_absence.calendar_days d
        WHERE d.company_id = p_company_id AND d.date BETWEEN make_date(p_year, 1, 1) AND make_date(p_year, 12, 31)
    ) THEN
        RAISE EXCEPTION 'calendar exists' USING ERRCODE = 'U0003', HINT = 'calendarExists';
    END IF;
    IF NOT EXISTS (SELECT FROM time_absence.holidays h WHERE extract(year FROM h.date) = p_year) THEN
        RAISE EXCEPTION 'no holidays for the year' USING ERRCODE = 'U0003', HINT = 'holidaysMissing';
    END IF;

    INSERT INTO time_absence.calendar_days (company_id, date, kind, hours)
    SELECT p_company_id, g.day,
           CASE
               WHEN h.date IS NOT NULL THEN 'holiday'
               WHEN extract(isodow FROM g.day) IN (6, 7) THEN 'dayOff'
               ELSE 'workday'
           END,
           CASE WHEN h.date IS NULL AND extract(isodow FROM g.day) NOT IN (6, 7) THEN 8 ELSE 0 END
    FROM (
        SELECT d::date AS day
        FROM generate_series(make_date(p_year, 1, 1), make_date(p_year, 12, 31), interval '1 day') d
    ) g
    LEFT JOIN time_absence.holidays h ON h.date = g.day;

    SELECT count(*)::integer INTO p_saturday_holidays
    FROM time_absence.holidays h
    WHERE extract(year FROM h.date) = p_year AND extract(isodow FROM h.date) = 6;
    PERFORM platform.audit('calendar', p_company_id::text || ':' || p_year, 'create', NULL,
        jsonb_build_object('company_id', p_company_id, 'year', p_year));
END;
$$;

--changeset hr:time_absence_app.calendar_year runOnChange:true splitStatements:false
--comment: Miesiące kalendarza spółki (R3): dni robocze, godziny pełnego etatu, święta w sobotę, wyznaczone dni wolne
--comment: i ostrzeżenie missing_designated (wymiar o 8 h za wysoki). Pusto, gdy rok nie ma kalendarza.
DROP FUNCTION IF EXISTS time_absence_app.calendar_year(uuid, integer);
CREATE FUNCTION time_absence_app.calendar_year(p_company_id uuid, p_year integer)
    RETURNS TABLE (
        month integer,
        work_days integer,
        hours numeric,
        saturday_holidays integer,
        designated_off integer,
        missing_designated boolean
    )
    LANGUAGE plpgsql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    IF NOT EXISTS (SELECT FROM platform.companies c WHERE c.id = p_company_id) THEN
        RAISE EXCEPTION 'company not found' USING ERRCODE = 'U0002';
    END IF;
    RETURN QUERY
    SELECT s.month, s.work_days, s.hours, s.saturday_holidays, s.designated_off,
           s.designated_off < s.saturday_holidays
    FROM time_absence.month_summary(p_company_id, p_year) s;
END;
$$;

--changeset hr:time_absence_app.calendar_month runOnChange:true splitStatements:false
--comment: Dni miesiąca kalendarza spółki z nazwą święta ze słownika (R2–R4). Pusto, gdy rok nie ma kalendarza.
DROP FUNCTION IF EXISTS time_absence_app.calendar_month(uuid, integer, integer);
CREATE FUNCTION time_absence_app.calendar_month(p_company_id uuid, p_year integer, p_month integer)
    RETURNS TABLE (date date, kind text, hours numeric, holiday_name text)
    LANGUAGE plpgsql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    IF NOT EXISTS (SELECT FROM platform.companies c WHERE c.id = p_company_id) THEN
        RAISE EXCEPTION 'company not found' USING ERRCODE = 'U0002';
    END IF;
    IF p_month IS NULL OR p_month NOT BETWEEN 1 AND 12 THEN
        PERFORM platform.fail_validation('{"month": ["invalid"]}');
    END IF;
    RETURN QUERY
    SELECT d.date, d.kind, d.hours, h.name
    FROM time_absence.calendar_days d
    LEFT JOIN time_absence.holidays h ON h.date = d.date
    WHERE d.company_id = p_company_id
      AND d.date BETWEEN make_date(p_year, p_month, 1) AND (make_date(p_year, p_month, 1) + interval '1 month')::date - 1
    ORDER BY d.date;
END;
$$;

--changeset hr:time_absence_app.calendar_day_set runOnChange:true splitStatements:false
--comment: Zmiana dnia kalendarza (R3, R4): rodzaj workday (godziny 0–12), dayOff, holiday (tylko święto ze słownika;
--comment: święta nie da się zrobić dniem roboczym), designatedOff (pn–pt, w miesiącu ze świętem w sobotę, nie więcej
--comment: niż takich świąt). Godziny dnia innego niż roboczy = 0.
CREATE OR REPLACE PROCEDURE time_absence_app.calendar_day_set(
    IN p_company_id uuid,
    IN p_date date,
    IN p_kind text,
    IN p_hours numeric
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_before time_absence.calendar_days%ROWTYPE;
    v_after time_absence.calendar_days%ROWTYPE;
    v_errors jsonb := '{}';
    v_holiday boolean;
    v_month_start date;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    -- Blokada spółki szereguje wyznaczanie dni wolnych w miesiącu.
    PERFORM FROM platform.companies c WHERE c.id = p_company_id FOR UPDATE;
    SELECT * INTO v_before FROM time_absence.calendar_days d
    WHERE d.company_id = p_company_id AND d.date = p_date FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'calendar day not found' USING ERRCODE = 'U0002';
    END IF;
    v_holiday := EXISTS (SELECT FROM time_absence.holidays h WHERE h.date = p_date);
    v_month_start := date_trunc('month', p_date)::date;

    IF p_kind IS NULL THEN
        v_errors := v_errors || '{"kind": ["required"]}';
    ELSIF p_kind NOT IN ('workday', 'dayOff', 'holiday', 'designatedOff')
        OR v_holiday <> (p_kind = 'holiday')
        OR (p_kind = 'designatedOff' AND (
            extract(isodow FROM p_date) IN (6, 7)
            OR (SELECT count(*) FROM time_absence.calendar_days d
                WHERE d.company_id = p_company_id AND d.kind = 'designatedOff' AND d.date <> p_date
                  AND d.date BETWEEN v_month_start AND (v_month_start + interval '1 month')::date - 1)
               >= (SELECT count(*) FROM time_absence.holidays h
                   WHERE h.date BETWEEN v_month_start AND (v_month_start + interval '1 month')::date - 1
                     AND extract(isodow FROM h.date) = 6)
        )) THEN
        v_errors := v_errors || '{"kind": ["invalid"]}';
    END IF;
    IF p_kind = 'workday' THEN
        IF p_hours IS NULL THEN
            v_errors := v_errors || '{"hours": ["required"]}';
        ELSIF p_hours < 0 OR p_hours > 12 OR p_hours <> round(p_hours, 2) THEN
            v_errors := v_errors || '{"hours": ["invalid"]}';
        END IF;
    ELSIF coalesce(p_hours, 0) <> 0 THEN
        v_errors := v_errors || '{"hours": ["invalid"]}';
    END IF;
    IF v_errors <> '{}' THEN
        PERFORM platform.fail_validation(v_errors);
    END IF;

    UPDATE time_absence.calendar_days d
    SET kind = p_kind, hours = CASE WHEN p_kind = 'workday' THEN p_hours ELSE 0 END
    WHERE d.company_id = p_company_id AND d.date = p_date
    RETURNING * INTO v_after;
    PERFORM platform.audit('calendar_day', p_company_id::text || ':' || p_date, 'update', to_jsonb(v_before),
        to_jsonb(v_after));
END;
$$;
