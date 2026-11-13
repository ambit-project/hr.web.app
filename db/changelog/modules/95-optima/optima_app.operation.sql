--liquibase formatted sql

--changeset hr:optima_app.operation_create runOnChange:true splitStatements:false
--comment: Zlecenie operacji dla agenta (Kadry, Administrator). ReadPayrollComponents — wszystkie bazy; pozostałe — jedna
--comment: spółka powiązana z bazą Optimy. AddEmployees: wskazane kartoteki spółki albo (NULL) wszystkie z umową
--comment: niezakończoną przed dziś. Eksport składników zapisuje pozycje pominięte (bez kwoty, bez powiązania).
CREATE OR REPLACE PROCEDURE optima_app.operation_create(
    IN p_kind text,
    IN p_company_id uuid,
    IN p_year integer,
    IN p_month integer,
    IN p_employee_ids uuid[],
    INOUT p_id uuid DEFAULT NULL
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_row optima.operations;
    v_errors jsonb := '{}';
    v_component_kind text;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');

    IF p_kind IS NULL OR p_kind NOT IN ('ReadPayrollComponents', 'AddEmployees', 'ExportDeductions',
                                        'ExportAllowances', 'ReadPayrolls') THEN
        PERFORM platform.fail_validation('{"kind": ["invalid"]}');
    END IF;
    v_row.kind := p_kind;

    IF p_kind <> 'ReadPayrollComponents' AND p_company_id IS NULL THEN
        v_errors := v_errors || '{"companyId": ["required"]}';
    END IF;
    IF p_kind IN ('ExportDeductions', 'ExportAllowances', 'ReadPayrolls') THEN
        IF p_year IS NULL OR p_year NOT BETWEEN 2000 AND 2100 THEN
            v_errors := v_errors || '{"year": ["invalid"]}';
        END IF;
        IF p_month IS NULL OR p_month NOT BETWEEN 1 AND 12 THEN
            v_errors := v_errors || '{"month": ["invalid"]}';
        END IF;
        v_row.year := p_year;
        v_row.month := p_month;
    END IF;
    IF v_errors <> '{}' THEN
        PERFORM platform.fail_validation(v_errors);
    END IF;

    IF p_kind <> 'ReadPayrollComponents' THEN
        v_row.company_id := p_company_id;
        v_row.database_name := optima.linked_database(p_company_id);
    END IF;

    IF p_kind = 'AddEmployees' THEN
        IF p_employee_ids IS NULL THEN
            SELECT array_agg(e.id ORDER BY e.acronym) INTO v_row.employee_ids
            FROM employees.employees e
            WHERE e.company_id = p_company_id
              AND EXISTS (
                  SELECT FROM employment.employments m
                  WHERE m.employee_id = e.id
                    AND coalesce(m.terminated_at, m.end_date, current_date) >= current_date
              );
        ELSE
            IF EXISTS (
                SELECT FROM unnest(p_employee_ids) i(id)
                WHERE NOT EXISTS (
                    SELECT FROM employees.employees e WHERE e.id = i.id AND e.company_id = p_company_id
                )
            ) THEN
                PERFORM platform.fail_validation('{"employeeIds": ["notFound"]}');
            END IF;
            SELECT array_agg(DISTINCT i.id) INTO v_row.employee_ids FROM unnest(p_employee_ids) i(id);
        END IF;
        IF coalesce(cardinality(v_row.employee_ids), 0) = 0 THEN
            PERFORM platform.fail_validation('{"employeeIds": ["required"]}');
        END IF;
    END IF;

    IF p_kind IN ('ExportDeductions', 'ExportAllowances') THEN
        v_component_kind := CASE p_kind WHEN 'ExportDeductions' THEN 'deduction' ELSE 'addition' END;
        SELECT coalesce(jsonb_agg(jsonb_build_object('id', x.item_id, 'employeeId', x.employee_id,
                                                     'reason', x.skip_reason)), '[]')
        INTO v_row.skipped
        FROM optima.export_items(p_company_id, v_row.database_name, v_component_kind, p_year, p_month) x
        WHERE x.skip_reason IS NOT NULL;
    END IF;

    INSERT INTO optima.operations (kind, company_id, database_name, year, month, employee_ids, skipped, created_by)
    VALUES (v_row.kind, v_row.company_id, v_row.database_name, v_row.year, v_row.month, v_row.employee_ids,
            coalesce(v_row.skipped, '[]'), platform.actor_id())
    RETURNING id INTO p_id;
    PERFORM platform.audit('optima_operation', p_id::text, 'create', NULL,
        jsonb_build_object('kind', v_row.kind, 'companyId', v_row.company_id, 'year', v_row.year,
                           'month', v_row.month, 'employeeCount', cardinality(v_row.employee_ids)));
END;
$$;

--changeset hr:optima_app.operation_list runOnChange:true splitStatements:false
--comment: Operacje agenta, najnowsze pierwsze (Kadry, Administrator).
DROP FUNCTION IF EXISTS optima_app.operation_list(text, text, uuid, integer, integer);
CREATE FUNCTION optima_app.operation_list(
    p_kind text DEFAULT NULL,
    p_state text DEFAULT NULL,
    p_company_id uuid DEFAULT NULL,
    p_limit integer DEFAULT 50,
    p_offset integer DEFAULT 0
)
    RETURNS TABLE (
        id uuid,
        kind text,
        state text,
        attempt integer,
        company_id uuid,
        company_short_name text,
        database_name text,
        year integer,
        month integer,
        created_at timestamptz,
        claimed_at timestamptz,
        finished_at timestamptz,
        error_code text,
        total_count bigint
    )
    LANGUAGE plpgsql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    RETURN QUERY
    SELECT o.id, o.kind, o.state, o.attempt, o.company_id, c.short_name, o.database_name, o.year, o.month,
           o.created_at, o.claimed_at, o.finished_at, o.error_code, count(*) OVER ()
    FROM optima.operations o
    LEFT JOIN platform.companies c ON c.id = o.company_id
    WHERE (p_kind IS NULL OR o.kind = p_kind)
      AND (p_state IS NULL OR o.state = p_state)
      AND (p_company_id IS NULL OR o.company_id = p_company_id)
    ORDER BY o.created_at DESC, o.id DESC
    LIMIT least(greatest(p_limit, 1), 100)
    OFFSET greatest(p_offset, 0);
END;
$$;

--changeset hr:optima_app.operation_get runOnChange:true splitStatements:false
--comment: Jedna operacja z logiem ostatniej próby i pozycjami pominiętymi (Kadry, Administrator).
DROP FUNCTION IF EXISTS optima_app.operation_get(uuid);
CREATE FUNCTION optima_app.operation_get(p_id uuid)
    RETURNS TABLE (
        id uuid,
        kind text,
        state text,
        attempt integer,
        company_id uuid,
        company_short_name text,
        database_name text,
        year integer,
        month integer,
        employee_count integer,
        created_at timestamptz,
        claimed_at timestamptz,
        finished_at timestamptz,
        succeeded boolean,
        error_code text,
        log jsonb,
        skipped jsonb
    )
    LANGUAGE plpgsql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    RETURN QUERY
    SELECT o.id, o.kind, o.state, o.attempt, o.company_id, c.short_name, o.database_name, o.year, o.month,
           cardinality(o.employee_ids), o.created_at, o.claimed_at, o.finished_at, o.succeeded, o.error_code,
           coalesce(o.log, '[]'), o.skipped
    FROM optima.operations o
    LEFT JOIN platform.companies c ON c.id = o.company_id
    WHERE o.id = p_id;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'operation not found' USING ERRCODE = 'U0002';
    END IF;
END;
$$;

--changeset hr:optima_app.operation_retry runOnChange:true splitStatements:false
--comment: Ponowna próba (Kadry, Administrator): ten sam id, attempt + 1. Tylko operacja nieudana albo wydana agentowi,
--comment: który nie odpowiedział; oczekująca → U0003 pending, udana → U0003 succeeded.
CREATE OR REPLACE PROCEDURE optima_app.operation_retry(IN p_id uuid, INOUT p_attempt integer DEFAULT NULL)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_op optima.operations;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    SELECT * INTO v_op FROM optima.operations o WHERE o.id = p_id FOR UPDATE;
    IF v_op.id IS NULL THEN
        RAISE EXCEPTION 'operation not found' USING ERRCODE = 'U0002';
    ELSIF v_op.state = 'pending' THEN
        RAISE EXCEPTION 'operation pending' USING ERRCODE = 'U0003', HINT = 'pending';
    ELSIF v_op.state = 'succeeded' THEN
        RAISE EXCEPTION 'operation succeeded' USING ERRCODE = 'U0003', HINT = 'succeeded';
    END IF;

    UPDATE optima.operations
    SET state = 'pending', attempt = attempt + 1, claimed_at = NULL, claimed_by = NULL, finished_at = NULL,
        succeeded = NULL, error_code = NULL, log = NULL
    WHERE id = p_id
    RETURNING attempt INTO p_attempt;
    PERFORM platform.audit('optima_operation', p_id::text, 'update',
        jsonb_build_object('attempt', v_op.attempt, 'state', v_op.state), jsonb_build_object('attempt', p_attempt));
END;
$$;
