--liquibase formatted sql

--changeset hr:optima_app.agent_claim runOnChange:true splitStatements:false
--comment: Agent bierze najstarszą oczekującą operację z rodzajów, które obsługuje (SKIP LOCKED: dwa równoległe claim
--comment: nie dostaną tej samej). Agent pyta co 500 ms, więc pusty claim to jeden odczyt indeksu, a „ostatnio widziany”
--comment: zapisuje się najwyżej raz na minutę (albo przy zmianie wersji agenta). Brak pracy → p_id NULL.
CREATE OR REPLACE PROCEDURE optima_app.agent_claim(
    IN p_key_hash bytea,
    IN p_agent_version text,
    IN p_kinds text[],
    INOUT p_id uuid DEFAULT NULL,
    INOUT p_kind text DEFAULT NULL,
    INOUT p_attempt integer DEFAULT NULL
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_key uuid := optima.agent_key_id(p_key_hash);
    v_version text := left(nullif(btrim(p_agent_version), ''), 50);
BEGIN
    UPDATE optima.agent_keys
    SET last_seen_at = now(), last_agent_version = v_version
    WHERE id = v_key
      AND (last_seen_at IS NULL OR last_seen_at < now() - interval '1 minute'
           OR last_agent_version IS DISTINCT FROM v_version);

    SELECT o.id, o.kind, o.attempt INTO p_id, p_kind, p_attempt
    FROM optima.operations o
    WHERE o.state = 'pending' AND o.kind = ANY (coalesce(p_kinds, '{}'))
    ORDER BY o.created_at, o.id
    LIMIT 1
    FOR UPDATE SKIP LOCKED;

    IF p_id IS NOT NULL THEN
        UPDATE optima.operations SET state = 'claimed', claimed_at = now(), claimed_by = v_key WHERE id = p_id;
    END IF;
END;
$$;

--changeset hr:optima_app.agent_employees runOnChange:true splitStatements:false
--comment: Kartoteki operacji AddEmployees dla agenta, który ją trzyma. Dane osobowe odszyfrowane tylko tu; każdy odczyt
--comment: trafia do audytu odczytów w imieniu operatora, który zlecił operację (zasób optima_export).
DROP FUNCTION IF EXISTS optima_app.agent_employees(bytea, uuid);
CREATE FUNCTION optima_app.agent_employees(p_key_hash bytea, p_operation_id uuid)
    RETURNS TABLE (
        database_name text,
        employee_id uuid,
        employee_code text,
        last_name text,
        first_name text,
        date_of_birth date,
        pesel text,
        email text,
        street text,
        postal_code text,
        city text,
        address_country_code text,
        identity_document text,
        position_name text,
        starts_on date,
        ends_on date
    )
    LANGUAGE plpgsql
    VOLATILE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_op optima.operations := optima.held_operation(p_key_hash, p_operation_id, 'AddEmployees');
BEGIN
    PERFORM optima.read_audit_as(v_op.created_by, i.id, 'optima_export', v_op.id::text)
    FROM unnest(v_op.employee_ids) i(id);

    RETURN QUERY
    SELECT v_op.database_name, e.id, e.acronym, e.last_name, e.first_name, e.birth_date,
           employees.decrypt(e.pesel_enc), e.email, a.street, a.postal_code, a.city, a.country,
           employees.decrypt(e.identity_document_enc), p.name, m.start_date,
           coalesce(m.terminated_at, m.end_date)
    FROM unnest(v_op.employee_ids) i(id)
    JOIN employees.employees e ON e.id = i.id AND e.company_id = v_op.company_id
    LEFT JOIN LATERAL optima.current_employment(e.id, current_date) m ON true
    LEFT JOIN LATERAL (
        SELECT t.position_id FROM employment.terms t
        WHERE t.employment_id = m.id AND t.valid_from <= greatest(m.start_date, current_date)
        ORDER BY t.valid_from DESC
        LIMIT 1
    ) tp ON true
    LEFT JOIN dictionaries.positions p ON p.id = tp.position_id
    LEFT JOIN LATERAL (
        SELECT ad.street, ad.postal_code, ad.city, ad.country FROM employees.addresses ad
        WHERE ad.employee_id = e.id AND ad.kind = 'residence' AND ad.valid_from <= current_date
        ORDER BY ad.valid_from DESC
        LIMIT 1
    ) a ON true
    ORDER BY e.acronym;
END;
$$;

--changeset hr:optima_app.agent_components runOnChange:true splitStatements:false
--comment: Potrącenia (ExportDeductions) albo dodatki (ExportAllowances) za miesiąc operacji dla agenta, który ją trzyma.
--comment: Tylko pozycje do wysłania; pominięte operacja zapisała przy zleceniu.
DROP FUNCTION IF EXISTS optima_app.agent_components(bytea, uuid, text);
CREATE FUNCTION optima_app.agent_components(p_key_hash bytea, p_operation_id uuid, p_kind text)
    RETURNS TABLE (
        database_name text,
        year integer,
        month integer,
        item_id uuid,
        employee_id uuid,
        employee_code text,
        optima_id integer,
        amount numeric
    )
    LANGUAGE plpgsql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_op optima.operations := optima.held_operation(p_key_hash, p_operation_id, p_kind);
BEGIN
    IF p_kind NOT IN ('ExportDeductions', 'ExportAllowances') THEN
        RAISE EXCEPTION 'operation not found' USING ERRCODE = 'U0002';
    END IF;
    RETURN QUERY
    SELECT v_op.database_name, v_op.year, v_op.month, x.item_id, x.employee_id, x.employee_code, x.optima_id,
           x.amount
    FROM optima.export_items(v_op.company_id, v_op.database_name,
                             CASE p_kind WHEN 'ExportDeductions' THEN 'deduction' ELSE 'addition' END,
                             v_op.year, v_op.month) x
    WHERE x.skip_reason IS NULL;
END;
$$;

--changeset hr:optima_app.agent_operation runOnChange:true splitStatements:false
--comment: Baza i miesiąc operacji danego rodzaju dla agenta, który ją trzyma (nagłówek odpowiedzi z pracą dla agenta).
DROP FUNCTION IF EXISTS optima_app.agent_operation(bytea, uuid, text);
CREATE FUNCTION optima_app.agent_operation(p_key_hash bytea, p_operation_id uuid, p_kind text)
    RETURNS TABLE (database_name text, year integer, month integer)
    LANGUAGE plpgsql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_op optima.operations := optima.held_operation(p_key_hash, p_operation_id, p_kind);
BEGIN
    RETURN QUERY SELECT v_op.database_name, v_op.year, v_op.month;
END;
$$;

--changeset hr:optima_app.agent_result runOnChange:true splitStatements:false
--comment: Wynik próby od agenta. Powtórzony wynik tej samej próby albo wynik starszej próby — bez zmian (agent wysyła,
--comment: dopóki nie dostanie odpowiedzi). Operacja nieistniejąca albo trzymana przez inny klucz → U0002.
--comment: ReadPayrollComponents zapisuje bazy i rodzaje płac; ReadPayrolls zastępuje listy płac bazy za miesiąc.
--comment: Zły kształt danych → U0001 payload (agent nie ponawia 400).
CREATE OR REPLACE PROCEDURE optima_app.agent_result(
    IN p_key_hash bytea,
    IN p_operation_id uuid,
    IN p_attempt integer,
    IN p_succeeded boolean,
    IN p_error_code text,
    IN p_log jsonb,
    IN p_payroll_components jsonb,
    IN p_payrolls jsonb
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_key uuid := optima.agent_key_id(p_key_hash);
    v_op optima.operations;
BEGIN
    SELECT * INTO v_op FROM optima.operations o WHERE o.id = p_operation_id FOR UPDATE;
    IF v_op.id IS NULL THEN
        RAISE EXCEPTION 'operation not found' USING ERRCODE = 'U0002';
    END IF;
    IF p_attempt IS NULL OR p_attempt < v_op.attempt
       OR (p_attempt = v_op.attempt AND v_op.state IN ('succeeded', 'failed')) THEN
        RETURN;
    END IF;
    IF v_op.state <> 'claimed' OR v_op.claimed_by <> v_key OR p_attempt <> v_op.attempt THEN
        RAISE EXCEPTION 'operation not held' USING ERRCODE = 'U0002';
    END IF;
    IF p_log IS NOT NULL AND jsonb_typeof(p_log) <> 'array' THEN
        PERFORM platform.fail_validation('{"log": ["invalid"]}');
    END IF;

    BEGIN
        IF v_op.kind = 'ReadPayrollComponents' AND p_payroll_components IS NOT NULL THEN
            PERFORM optima.store_pay_types(p_payroll_components);
        ELSIF v_op.kind = 'ReadPayrolls' AND p_payrolls IS NOT NULL THEN
            PERFORM optima.store_payrolls(v_op, p_payrolls);
        END IF;
    EXCEPTION
        WHEN invalid_text_representation OR invalid_datetime_format OR datetime_field_overflow
            OR numeric_value_out_of_range OR not_null_violation OR check_violation OR unique_violation
            OR invalid_parameter_value OR wrong_object_type THEN
            PERFORM platform.fail_validation('{"payload": ["invalid"]}');
    END;

    UPDATE optima.operations
    SET state = CASE WHEN coalesce(p_succeeded, false) THEN 'succeeded' ELSE 'failed' END,
        succeeded = coalesce(p_succeeded, false),
        error_code = left(nullif(btrim(p_error_code), ''), 100),
        log = p_log,
        finished_at = now()
    WHERE id = v_op.id;
END;
$$;
