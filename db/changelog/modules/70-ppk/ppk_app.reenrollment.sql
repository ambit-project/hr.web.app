--liquibase formatted sql

--changeset hr:ppk_app.reenrollment_get runOnChange:true splitStatements:false
--comment: Cykl ponownego zapisu spółki w roku (R12, R13; Kadry, Administrator): ostatni dzień na informację, dzień
--comment: wznowienia (parametry prawne), data przekazania informacji i wykonania wznowienia. Rok spoza cyklu — U0001
--comment: {"year": ["notCycleYear"]}.
DROP FUNCTION IF EXISTS ppk_app.reenrollment_get(uuid, integer);
CREATE FUNCTION ppk_app.reenrollment_get(p_company_id uuid, p_year integer)
    RETURNS TABLE (
        year integer,
        information_deadline date,
        restart_date date,
        informed_at date,
        applied_at timestamptz
    )
    LANGUAGE plpgsql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_cycle record;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    IF NOT EXISTS (SELECT FROM platform.companies c WHERE c.id = p_company_id) THEN
        RAISE EXCEPTION 'company not found' USING ERRCODE = 'U0002';
    END IF;
    SELECT * INTO v_cycle FROM ppk.cycle_get(p_year);
    RETURN QUERY
    SELECT p_year, v_cycle.information_deadline, v_cycle.restart_date, c.informed_at, c.applied_at
    FROM (SELECT 1) one
    LEFT JOIN ppk.reenrollment_cycles c ON c.company_id = p_company_id AND c.year = p_year;
END;
$$;

--changeset hr:ppk_app.reenrollment_list runOnChange:true splitStatements:false
--comment: Lista ponownego zapisu spółki w roku cyklu (R13; Kadry, Administrator) — opis w ppk.reenrollment_rows.
DROP FUNCTION IF EXISTS ppk_app.reenrollment_list(uuid, integer);
CREATE FUNCTION ppk_app.reenrollment_list(p_company_id uuid, p_year integer)
    RETURNS TABLE (
        employee_id uuid,
        employee_number text,
        first_name text,
        last_name text,
        birth_date date,
        age integer,
        resigned_from date,
        group_kind text,
        resigned_after_information boolean,
        restarted boolean
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
    RETURN QUERY SELECT * FROM ppk.reenrollment_rows(p_company_id, p_year);
END;
$$;

--changeset hr:ppk_app.reenrollment_mark_informed runOnChange:true splitStatements:false
--comment: Data przekazania informacji o ponownym zapisie (R13): w roku cyklu, przed dniem wznowienia, nie z przyszłości.
--comment: Po wykonaniu wznowienia — U0003 applied.
CREATE OR REPLACE PROCEDURE ppk_app.reenrollment_mark_informed(
    IN p_company_id uuid,
    IN p_year integer,
    IN p_informed_at date
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_cycle record;
    v_before ppk.reenrollment_cycles%ROWTYPE;
    v_after ppk.reenrollment_cycles%ROWTYPE;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    PERFORM FROM platform.companies c WHERE c.id = p_company_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'company not found' USING ERRCODE = 'U0002';
    END IF;
    SELECT * INTO v_cycle FROM ppk.cycle_get(p_year);
    IF p_informed_at IS NULL THEN
        PERFORM platform.fail_validation('{"informedAt": ["required"]}');
    ELSIF p_informed_at > current_date THEN
        PERFORM platform.fail_validation('{"informedAt": ["future"]}');
    ELSIF p_informed_at < make_date(p_year, 1, 1) OR p_informed_at >= v_cycle.restart_date THEN
        PERFORM platform.fail_validation('{"informedAt": ["invalid"]}');
    END IF;

    SELECT * INTO v_before FROM ppk.reenrollment_cycles c WHERE c.company_id = p_company_id AND c.year = p_year;
    IF v_before.applied_at IS NOT NULL THEN
        RAISE EXCEPTION 'cycle already applied' USING ERRCODE = 'U0003', HINT = 'applied';
    END IF;
    INSERT INTO ppk.reenrollment_cycles (company_id, year, informed_at)
    VALUES (p_company_id, p_year, p_informed_at)
    ON CONFLICT (company_id, year) DO UPDATE SET informed_at = excluded.informed_at
    RETURNING * INTO v_after;
    PERFORM platform.audit('ppk_reenrollment_cycle', p_company_id::text || ':' || p_year::text,
        CASE WHEN v_before.year IS NULL THEN 'create' ELSE 'update' END,
        CASE WHEN v_before.year IS NULL THEN NULL ELSE to_jsonb(v_before) END, to_jsonb(v_after));
END;
$$;

--changeset hr:ppk_app.reenrollment_apply runOnChange:true splitStatements:false
--comment: Wznowienie wpłat z cyklu (R13): każdej osobie z grupy restart, jeszcze nie wznowionej i bez rezygnacji złożonej
--comment: po informacji — deklaracja autoRestart od dnia wznowienia (rezygnacja kończy się dzień wcześniej, nowy okres
--comment: stawek). Wymaga daty informacji (U0003 notInformed). Powtórzenie wznawia tylko brakujących; p_count — ilu
--comment: wznowiono teraz.
CREATE OR REPLACE PROCEDURE ppk_app.reenrollment_apply(
    IN p_company_id uuid,
    IN p_year integer,
    INOUT p_count integer
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_cycle record;
    v_before ppk.reenrollment_cycles%ROWTYPE;
    v_after ppk.reenrollment_cycles%ROWTYPE;
    v_person record;
    v_row ppk.declarations%ROWTYPE;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    PERFORM FROM platform.companies c WHERE c.id = p_company_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'company not found' USING ERRCODE = 'U0002';
    END IF;
    SELECT * INTO v_cycle FROM ppk.cycle_get(p_year);
    SELECT * INTO v_before FROM ppk.reenrollment_cycles c
    WHERE c.company_id = p_company_id AND c.year = p_year FOR UPDATE;
    IF v_before.informed_at IS NULL THEN
        RAISE EXCEPTION 'information not recorded' USING ERRCODE = 'U0003', HINT = 'notInformed';
    END IF;

    p_count := 0;
    FOR v_person IN
        SELECT r.employee_id FROM ppk.reenrollment_rows(p_company_id, p_year) r
        WHERE r.group_kind = 'restart' AND NOT r.restarted AND NOT r.resigned_after_information
    LOOP
        PERFORM FROM employees.employees e WHERE e.id = v_person.employee_id FOR UPDATE;
        INSERT INTO ppk.declarations (employee_id, kind, submitted_at, effective_from, cycle_year, created_by)
        VALUES (v_person.employee_id, 'autoRestart', v_cycle.restart_date, v_cycle.restart_date, p_year,
                platform.actor_id())
        RETURNING * INTO v_row;
        PERFORM ppk.rebuild_rates(v_person.employee_id);
        PERFORM platform.audit('ppk_declaration', v_row.id::text, 'create', NULL, to_jsonb(v_row));
        p_count := p_count + 1;
    END LOOP;

    UPDATE ppk.reenrollment_cycles c SET applied_at = now(), applied_by = platform.actor_id()
    WHERE c.company_id = p_company_id AND c.year = p_year
    RETURNING * INTO v_after;
    PERFORM platform.audit('ppk_reenrollment_cycle', p_company_id::text || ':' || p_year::text, 'update',
        to_jsonb(v_before), to_jsonb(v_after));
END;
$$;
