--liquibase formatted sql

--changeset hr:ppk_app.declaration_list runOnChange:true splitStatements:false
--comment: Deklaracje i wnioski kartoteki, od najnowszej (R9, R14). Dostęp jak participation_get. kind: resignation,
--comment: restartRequest, enrollmentRequest, reducedRate, additionalRate, autoRestart (wznowienie z cyklu co 4 lata).
DROP FUNCTION IF EXISTS ppk_app.declaration_list(uuid);
CREATE FUNCTION ppk_app.declaration_list(p_employee_id uuid)
    RETURNS TABLE (
        id uuid,
        kind text,
        submitted_at date,
        effective_from date,
        rate numeric,
        cycle_year integer,
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
    SELECT d.id, d.kind, d.submitted_at, d.effective_from, d.rate, d.cycle_year, d.xmin::text::bigint
    FROM ppk.declarations d
    WHERE d.employee_id = p_employee_id
    ORDER BY d.effective_from DESC, d.submitted_at DESC, d.created_at DESC;
END;
$$;

--changeset hr:ppk_app.declaration_create runOnChange:true splitStatements:false
--comment: Nowa deklaracja albo wniosek (R9, R10) z datą złożenia (nie z przyszłości). rate w procentach tylko przy
--comment: reducedRate (0,5–2) i additionalRate (0–2). Konflikty (U0003): alreadyResigned (ostatnia deklaracja stanu
--comment: to rezygnacja, chyba że po informacji o ponownym zapisie w roku cyklu), notResigned (wniosek o wznowienie
--comment: bez rezygnacji), notEnrolled (stawki bez uczestnictwa).
--comment: Rezygnacja złożona po informacji o ponownym zapisie usuwa oczekujące wznowienie z cyklu (R13).
CREATE OR REPLACE PROCEDURE ppk_app.declaration_create(
    IN p_employee_id uuid,
    IN p_kind text,
    IN p_submitted_at date,
    IN p_rate numeric,
    INOUT p_id uuid
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_effective date;
    v_last_kind text;
    v_row ppk.declarations%ROWTYPE;
    v_removed ppk.declarations%ROWTYPE;
    v_errors jsonb := '{}';
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    PERFORM FROM employees.employees e WHERE e.id = p_employee_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'employee not found' USING ERRCODE = 'U0002';
    END IF;

    IF p_kind IS NULL THEN
        v_errors := v_errors || '{"kind": ["required"]}';
    ELSIF p_kind NOT IN ('resignation', 'restartRequest', 'enrollmentRequest', 'reducedRate', 'additionalRate') THEN
        v_errors := v_errors || '{"kind": ["invalid"]}';
    END IF;
    IF p_submitted_at IS NULL THEN
        v_errors := v_errors || '{"submittedAt": ["required"]}';
    ELSIF p_submitted_at > current_date THEN
        v_errors := v_errors || '{"submittedAt": ["future"]}';
    END IF;
    IF p_kind IN ('reducedRate', 'additionalRate') THEN
        IF p_rate IS NULL THEN
            v_errors := v_errors || '{"rate": ["required"]}';
        ELSIF p_rate <> round(p_rate, 2)
            OR (p_kind = 'reducedRate' AND p_rate NOT BETWEEN 0.5 AND 2)
            OR (p_kind = 'additionalRate' AND p_rate NOT BETWEEN 0 AND 2) THEN
            v_errors := v_errors || '{"rate": ["invalid"]}';
        END IF;
    ELSIF p_rate IS NOT NULL AND p_kind IS NOT NULL THEN
        v_errors := v_errors || '{"rate": ["invalid"]}';
    END IF;
    IF v_errors <> '{}' THEN
        PERFORM platform.fail_validation(v_errors);
    END IF;

    v_last_kind := ppk.last_state_kind(p_employee_id, NULL);
    -- Ponowna rezygnacja tylko po informacji o ponownym zapisie w roku cyklu (art. 23 ust. 7; R13).
    IF p_kind = 'resignation' AND v_last_kind = 'resignation' AND NOT EXISTS (
        SELECT FROM ppk.reenrollment_cycles c JOIN employees.employees e ON e.company_id = c.company_id
        WHERE e.id = p_employee_id AND c.year = extract(year FROM p_submitted_at) AND c.informed_at <= p_submitted_at
    ) THEN
        RAISE EXCEPTION 'already resigned' USING ERRCODE = 'U0003', HINT = 'alreadyResigned';
    END IF;
    IF p_kind = 'restartRequest' AND v_last_kind IS DISTINCT FROM 'resignation' THEN
        RAISE EXCEPTION 'not resigned' USING ERRCODE = 'U0003', HINT = 'notResigned';
    END IF;
    IF p_kind IN ('reducedRate', 'additionalRate')
        AND NOT EXISTS (SELECT FROM ppk.participations p WHERE p.employee_id = p_employee_id) THEN
        RAISE EXCEPTION 'not enrolled' USING ERRCODE = 'U0003', HINT = 'notEnrolled';
    END IF;

    v_effective := ppk.effective_from(p_kind, p_submitted_at);
    INSERT INTO ppk.declarations (employee_id, kind, submitted_at, effective_from, rate, created_by)
    VALUES (p_employee_id, p_kind, p_submitted_at, v_effective, p_rate, platform.actor_id())
    RETURNING * INTO v_row;
    p_id := v_row.id;
    PERFORM platform.audit('ppk_declaration', p_id::text, 'create', NULL, to_jsonb(v_row));

    IF p_kind = 'resignation' THEN
        FOR v_removed IN
            DELETE FROM ppk.declarations a
            USING employees.employees e, ppk.reenrollment_cycles c
            WHERE a.employee_id = p_employee_id AND a.kind = 'autoRestart' AND a.effective_from > v_effective
              AND e.id = a.employee_id AND c.company_id = e.company_id AND c.year = a.cycle_year
              AND c.informed_at <= p_submitted_at
            RETURNING a.*
        LOOP
            PERFORM platform.audit('ppk_declaration', v_removed.id::text, 'delete', to_jsonb(v_removed), NULL);
        END LOOP;
    END IF;
    PERFORM ppk.rebuild_rates(p_employee_id);
END;
$$;

--changeset hr:ppk_app.declaration_delete runOnChange:true splitStatements:false
--comment: Usunięcie deklaracji wpisanej pomyłkowo (R9), z wersją wiersza; okresy stawek odbudowane.
CREATE OR REPLACE PROCEDURE ppk_app.declaration_delete(IN p_id uuid, IN p_row_version bigint)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_row ppk.declarations%ROWTYPE;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    SELECT * INTO v_row FROM ppk.declarations d WHERE d.id = p_id;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'declaration not found' USING ERRCODE = 'U0002';
    END IF;
    PERFORM FROM employees.employees e WHERE e.id = v_row.employee_id FOR UPDATE;
    PERFORM FROM ppk.declarations d WHERE d.id = p_id FOR UPDATE;
    IF (SELECT d.xmin::text::bigint FROM ppk.declarations d WHERE d.id = p_id) IS DISTINCT FROM p_row_version THEN
        RAISE EXCEPTION 'declaration changed' USING ERRCODE = 'U0003';
    END IF;
    DELETE FROM ppk.declarations d WHERE d.id = p_id;
    PERFORM ppk.rebuild_rates(v_row.employee_id);
    PERFORM platform.audit('ppk_declaration', p_id::text, 'delete', to_jsonb(v_row), NULL);
END;
$$;
