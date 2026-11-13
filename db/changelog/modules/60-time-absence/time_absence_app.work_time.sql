--liquibase formatted sql

--changeset hr:time_absence_app.overtime_list runOnChange:true splitStatements:false
--comment: Nadgodziny umowy o pracę w roku (R16; Kadry, Administrator).
DROP FUNCTION IF EXISTS time_absence_app.overtime_list(uuid, integer);
CREATE FUNCTION time_absence_app.overtime_list(p_employment_id uuid, p_year integer)
    RETURNS TABLE (id uuid, date date, hours numeric, rate integer, row_version bigint)
    LANGUAGE plpgsql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    PERFORM time_absence.employment_get(p_employment_id, ARRAY['employment']);
    RETURN QUERY
    SELECT o.id, o.date, o.hours, o.rate, o.xmin::text::bigint
    FROM time_absence.overtime_entries o
    WHERE o.employment_id = p_employment_id AND extract(year FROM o.date) = p_year
    ORDER BY o.date, o.rate;
END;
$$;

--changeset hr:time_absence_app.overtime_summary runOnChange:true splitStatements:false
--comment: Suma nadgodzin roku i ostrzeżenie po przekroczeniu 150 h (KP art. 151 § 3 — limit można zmienić układem,
--comment: regulaminem albo umową, więc tylko ostrzeżenie; R16).
DROP FUNCTION IF EXISTS time_absence_app.overtime_summary(uuid, integer);
CREATE FUNCTION time_absence_app.overtime_summary(p_employment_id uuid, p_year integer)
    RETURNS TABLE (total_hours numeric, hours50 numeric, hours100 numeric, limit_hours numeric, above_limit boolean)
    LANGUAGE plpgsql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    PERFORM time_absence.employment_get(p_employment_id, ARRAY['employment']);
    RETURN QUERY
    SELECT coalesce(sum(o.hours), 0),
           coalesce(sum(o.hours) FILTER (WHERE o.rate = 50), 0),
           coalesce(sum(o.hours) FILTER (WHERE o.rate = 100), 0),
           150::numeric,
           coalesce(sum(o.hours), 0) > 150
    FROM time_absence.overtime_entries o
    WHERE o.employment_id = p_employment_id AND extract(year FROM o.date) = p_year;
END;
$$;

--changeset hr:time_absence_app.overtime_add runOnChange:true splitStatements:false
--comment: Nadgodziny w dniu (R16): 0,5–16 h, rodzaj dodatku 50 albo 100 (KP art. 151¹ § 1 — wybierają Kadry).
CREATE OR REPLACE PROCEDURE time_absence_app.overtime_add(
    IN p_employment_id uuid,
    IN p_date date,
    IN p_hours numeric,
    IN p_rate integer,
    INOUT p_id uuid
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_employment employment.employments%ROWTYPE;
    v_errors jsonb := '{}';
    v_row time_absence.overtime_entries%ROWTYPE;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    PERFORM FROM employment.employments m WHERE m.id = p_employment_id FOR UPDATE;
    v_employment := time_absence.employment_get(p_employment_id, ARRAY['employment']);
    IF p_date IS NULL THEN
        v_errors := v_errors || '{"date": ["required"]}';
    ELSIF NOT employment.period(v_employment.start_date, v_employment.end_date, v_employment.terminated_at)
        @> p_date THEN
        v_errors := v_errors || '{"date": ["outsidePeriod"]}';
    ELSIF EXISTS (
        SELECT FROM time_absence.overtime_entries o
        WHERE o.employment_id = p_employment_id AND o.date = p_date AND o.rate = p_rate
    ) THEN
        v_errors := v_errors || '{"date": ["duplicate"]}';
    END IF;
    IF p_hours IS NULL THEN
        v_errors := v_errors || '{"hours": ["required"]}';
    ELSIF p_hours < 0.5 OR p_hours > 16 OR p_hours <> round(p_hours, 2) THEN
        v_errors := v_errors || '{"hours": ["invalid"]}';
    END IF;
    IF p_rate IS NULL THEN
        v_errors := v_errors || '{"rate": ["required"]}';
    ELSIF p_rate NOT IN (50, 100) THEN
        v_errors := v_errors || '{"rate": ["invalid"]}';
    END IF;
    IF v_errors <> '{}' THEN
        PERFORM platform.fail_validation(v_errors);
    END IF;

    INSERT INTO time_absence.overtime_entries (employment_id, date, hours, rate, created_by)
    VALUES (p_employment_id, p_date, p_hours, p_rate, platform.actor_id())
    RETURNING * INTO v_row;
    p_id := v_row.id;
    PERFORM platform.audit('overtime', p_id::text, 'create', NULL, to_jsonb(v_row));
END;
$$;

--changeset hr:time_absence_app.overtime_delete runOnChange:true splitStatements:false
--comment: Usunięcie wpisu nadgodzin (R16), z wersją wiersza.
CREATE OR REPLACE PROCEDURE time_absence_app.overtime_delete(IN p_id uuid, IN p_row_version bigint)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_row time_absence.overtime_entries%ROWTYPE;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    SELECT * INTO v_row FROM time_absence.overtime_entries o WHERE o.id = p_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'overtime entry not found' USING ERRCODE = 'U0002';
    END IF;
    IF (SELECT o.xmin::text::bigint FROM time_absence.overtime_entries o WHERE o.id = p_id) <> p_row_version THEN
        RAISE EXCEPTION 'overtime entry changed' USING ERRCODE = 'U0003';
    END IF;
    DELETE FROM time_absence.overtime_entries o WHERE o.id = p_id;
    PERFORM platform.audit('overtime', p_id::text, 'delete', to_jsonb(v_row), NULL);
END;
$$;

--changeset hr:time_absence_app.mandate_hours_list runOnChange:true splitStatements:false
--comment: Godziny zlecenia nachodzące na rok (R17; Kadry, Administrator). confirmed = false — lista płac ich nie
--comment: weźmie (ostrzeżenie).
DROP FUNCTION IF EXISTS time_absence_app.mandate_hours_list(uuid, integer);
CREATE FUNCTION time_absence_app.mandate_hours_list(p_employment_id uuid, p_year integer)
    RETURNS TABLE (
        id uuid,
        date_from date,
        date_to date,
        hours numeric,
        confirmed_at date,
        confirmation_form text,
        confirmed boolean,
        row_version bigint
    )
    LANGUAGE plpgsql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    PERFORM time_absence.employment_get(p_employment_id, ARRAY['mandate']);
    RETURN QUERY
    SELECT h.id, h.date_from, h.date_to, h.hours, h.confirmed_at, h.confirmation_form, h.confirmed_at IS NOT NULL,
           h.xmin::text::bigint
    FROM time_absence.mandate_hours h
    WHERE h.employment_id = p_employment_id
      AND daterange(h.date_from, h.date_to, '[]') && daterange(make_date(p_year, 1, 1), make_date(p_year, 12, 31), '[]')
    ORDER BY h.date_from;
END;
$$;

--changeset hr:time_absence.mandate_hours_validate runOnChange:true splitStatements:false
--comment: Błędy pól godzin zlecenia (R17). p_id = NULL przy nowym wpisie.
CREATE OR REPLACE FUNCTION time_absence.mandate_hours_validate(
    p_id uuid,
    p_employment employment.employments,
    p_from date,
    p_to date,
    p_hours numeric,
    p_confirmed_at date,
    p_form text
)
    RETURNS jsonb
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_errors jsonb := '{}';
BEGIN
    IF p_from IS NULL THEN
        v_errors := v_errors || '{"dateFrom": ["required"]}';
    END IF;
    IF p_to IS NULL THEN
        v_errors := v_errors || '{"dateTo": ["required"]}';
    ELSIF p_to < p_from THEN
        v_errors := v_errors || '{"dateTo": ["beforeFrom"]}';
    END IF;
    IF p_from IS NOT NULL AND p_to >= p_from THEN
        IF NOT employment.period(p_employment.start_date, p_employment.end_date, p_employment.terminated_at)
            @> daterange(p_from, p_to, '[]') THEN
            v_errors := v_errors || '{"dateFrom": ["outsidePeriod"]}';
        ELSIF EXISTS (
            SELECT FROM time_absence.mandate_hours h
            WHERE h.employment_id = p_employment.id AND h.id IS DISTINCT FROM p_id
              AND daterange(h.date_from, h.date_to, '[]') && daterange(p_from, p_to, '[]')
        ) THEN
            v_errors := v_errors || '{"dateFrom": ["overlap"]}';
        END IF;
    END IF;
    IF p_hours IS NULL THEN
        v_errors := v_errors || '{"hours": ["required"]}';
    ELSIF p_hours <= 0 OR p_hours > 744 OR p_hours <> round(p_hours, 2) THEN
        v_errors := v_errors || '{"hours": ["invalid"]}';
    END IF;
    IF p_form IS NOT NULL AND p_form NOT IN ('written', 'electronic', 'documentary') THEN
        v_errors := v_errors || '{"confirmationForm": ["invalid"]}';
    ELSIF p_form IS NULL AND p_confirmed_at IS NOT NULL THEN
        v_errors := v_errors || '{"confirmationForm": ["required"]}';
    END IF;
    IF p_confirmed_at IS NULL AND p_form IS NOT NULL THEN
        v_errors := v_errors || '{"confirmedAt": ["required"]}';
    ELSIF p_confirmed_at > current_date THEN
        v_errors := v_errors || '{"confirmedAt": ["future"]}';
    ELSIF p_confirmed_at < p_from THEN
        v_errors := v_errors || '{"confirmedAt": ["beforeFrom"]}';
    END IF;
    RETURN v_errors;
END;
$$;

--changeset hr:time_absence_app.mandate_hours_add runOnChange:true splitStatements:false
--comment: Godziny zlecenia za okres (R17): data i sposób potwierdzenia przez zleceniobiorcę (ustawa o minimalnym
--comment: wynagrodzeniu art. 8b) albo oba puste — wpis bez potwierdzenia.
CREATE OR REPLACE PROCEDURE time_absence_app.mandate_hours_add(
    IN p_employment_id uuid,
    IN p_date_from date,
    IN p_date_to date,
    IN p_hours numeric,
    IN p_confirmed_at date,
    IN p_confirmation_form text,
    INOUT p_id uuid
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_employment employment.employments%ROWTYPE;
    v_errors jsonb;
    v_row time_absence.mandate_hours%ROWTYPE;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    PERFORM FROM employment.employments m WHERE m.id = p_employment_id FOR UPDATE;
    v_employment := time_absence.employment_get(p_employment_id, ARRAY['mandate']);
    v_errors := time_absence.mandate_hours_validate(NULL, v_employment, p_date_from, p_date_to, p_hours,
        p_confirmed_at, p_confirmation_form);
    IF v_errors <> '{}' THEN
        PERFORM platform.fail_validation(v_errors);
    END IF;

    INSERT INTO time_absence.mandate_hours (employment_id, date_from, date_to, hours, confirmed_at, confirmation_form,
                                            created_by)
    VALUES (p_employment_id, p_date_from, p_date_to, p_hours, p_confirmed_at, p_confirmation_form,
            platform.actor_id())
    RETURNING * INTO v_row;
    p_id := v_row.id;
    PERFORM platform.audit('mandate_hours', p_id::text, 'create', NULL, to_jsonb(v_row));
END;
$$;

--changeset hr:time_absence_app.mandate_hours_update runOnChange:true splitStatements:false
--comment: Zmiana godzin zlecenia, w tym potwierdzenie (R17), z wersją wiersza.
CREATE OR REPLACE PROCEDURE time_absence_app.mandate_hours_update(
    IN p_id uuid,
    IN p_row_version bigint,
    IN p_date_from date,
    IN p_date_to date,
    IN p_hours numeric,
    IN p_confirmed_at date,
    IN p_confirmation_form text
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_employment employment.employments%ROWTYPE;
    v_before time_absence.mandate_hours%ROWTYPE;
    v_after time_absence.mandate_hours%ROWTYPE;
    v_errors jsonb;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    SELECT * INTO v_before FROM time_absence.mandate_hours h WHERE h.id = p_id;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'mandate hours not found' USING ERRCODE = 'U0002';
    END IF;
    PERFORM FROM employment.employments m WHERE m.id = v_before.employment_id FOR UPDATE;
    PERFORM FROM time_absence.mandate_hours h WHERE h.id = p_id FOR UPDATE;
    IF (SELECT h.xmin::text::bigint FROM time_absence.mandate_hours h WHERE h.id = p_id) <> p_row_version THEN
        RAISE EXCEPTION 'mandate hours changed' USING ERRCODE = 'U0003';
    END IF;
    v_employment := time_absence.employment_get(v_before.employment_id, ARRAY['mandate']);
    v_errors := time_absence.mandate_hours_validate(p_id, v_employment, p_date_from, p_date_to, p_hours,
        p_confirmed_at, p_confirmation_form);
    IF v_errors <> '{}' THEN
        PERFORM platform.fail_validation(v_errors);
    END IF;

    UPDATE time_absence.mandate_hours h
    SET date_from = p_date_from, date_to = p_date_to, hours = p_hours, confirmed_at = p_confirmed_at,
        confirmation_form = p_confirmation_form
    WHERE h.id = p_id
    RETURNING * INTO v_after;
    PERFORM platform.audit('mandate_hours', p_id::text, 'update', to_jsonb(v_before), to_jsonb(v_after));
END;
$$;

--changeset hr:time_absence_app.mandate_hours_delete runOnChange:true splitStatements:false
--comment: Usunięcie godzin zlecenia (R17), z wersją wiersza.
CREATE OR REPLACE PROCEDURE time_absence_app.mandate_hours_delete(IN p_id uuid, IN p_row_version bigint)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_row time_absence.mandate_hours%ROWTYPE;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    SELECT * INTO v_row FROM time_absence.mandate_hours h WHERE h.id = p_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'mandate hours not found' USING ERRCODE = 'U0002';
    END IF;
    IF (SELECT h.xmin::text::bigint FROM time_absence.mandate_hours h WHERE h.id = p_id) <> p_row_version THEN
        RAISE EXCEPTION 'mandate hours changed' USING ERRCODE = 'U0003';
    END IF;
    DELETE FROM time_absence.mandate_hours h WHERE h.id = p_id;
    PERFORM platform.audit('mandate_hours', p_id::text, 'delete', to_jsonb(v_row), NULL);
END;
$$;
