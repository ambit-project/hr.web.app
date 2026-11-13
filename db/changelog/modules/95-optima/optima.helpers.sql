--liquibase formatted sql

--changeset hr:optima.agent_key_id runOnChange:true splitStatements:false
--comment: Id aktywnego klucza agenta po skrócie tokenu; nieznany albo odwołany → U0002 (API odpowiada wtedy 401 wcześniej).
CREATE OR REPLACE FUNCTION optima.agent_key_id(p_key_hash bytea)
    RETURNS uuid
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_id uuid;
BEGIN
    SELECT k.id INTO v_id FROM optima.agent_keys k WHERE k.key_hash = p_key_hash AND k.revoked_at IS NULL;
    IF v_id IS NULL THEN
        RAISE EXCEPTION 'agent key not found' USING ERRCODE = 'U0002';
    END IF;
    RETURN v_id;
END;
$$;

--changeset hr:optima.held_operation runOnChange:true splitStatements:false
--comment: Operacja danego rodzaju, którą trzyma ten klucz (stan claimed); inaczej U0002 — agent dostaje 404, który jest ostateczny.
CREATE OR REPLACE FUNCTION optima.held_operation(p_key_hash bytea, p_operation_id uuid, p_kind text)
    RETURNS optima.operations
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_key uuid := optima.agent_key_id(p_key_hash);
    v_op optima.operations;
BEGIN
    SELECT * INTO v_op FROM optima.operations o
    WHERE o.id = p_operation_id AND o.kind = p_kind AND o.state = 'claimed' AND o.claimed_by = v_key;
    IF v_op.id IS NULL THEN
        RAISE EXCEPTION 'operation not found' USING ERRCODE = 'U0002';
    END IF;
    RETURN v_op;
END;
$$;

--changeset hr:optima.read_audit_as runOnChange:true splitStatements:false
--comment: Audyt odczytu w imieniu operatora, który zlecił operację (agent nie ma konta w aplikacji).
CREATE OR REPLACE FUNCTION optima.read_audit_as(
    p_actor_id uuid,
    p_employee_id uuid,
    p_resource text,
    p_resource_id text
)
    RETURNS void
    LANGUAGE sql
    SET search_path = pg_catalog, pg_temp
AS $$
    INSERT INTO platform.read_audit_entries (actor_id, employee_id, resource, resource_id)
    SELECT p_actor_id, p_employee_id, p_resource, p_resource_id
    WHERE p_actor_id IS NOT NULL;
$$;

--changeset hr:optima.try_uuid runOnChange:true splitStatements:false
--comment: Tekst jako uuid albo NULL (PRA_ImportRowId z Optimy bywa pusty albo nie jest naszym id).
CREATE OR REPLACE FUNCTION optima.try_uuid(p_value text)
    RETURNS uuid
    LANGUAGE sql
    IMMUTABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT CASE
        WHEN p_value ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' THEN p_value::uuid
    END;
$$;

--changeset hr:optima.database_seen runOnChange:true splitStatements:false
--comment: Zapisuje bazę firmy zgłoszoną przez agenta; nową bazę wiąże ze spółką o tym samym NIP, jeśli obie są wolne.
CREATE OR REPLACE FUNCTION optima.database_seen(p_database_name text, p_tax_number text)
    RETURNS void
    LANGUAGE plpgsql
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_tax text := nullif(platform.digits(p_tax_number), '');
    v_company_id uuid;
BEGIN
    INSERT INTO optima.databases (database_name, tax_number, last_seen_at)
    VALUES (p_database_name, v_tax, now())
    ON CONFLICT (database_name) DO UPDATE
        SET tax_number = coalesce(excluded.tax_number, optima.databases.tax_number),
            last_seen_at = excluded.last_seen_at;

    IF v_tax IS NULL OR EXISTS (SELECT FROM optima.company_links l WHERE l.database_name = p_database_name) THEN
        RETURN;
    END IF;

    SELECT c.id INTO v_company_id FROM platform.companies c
    WHERE c.nip = v_tax AND NOT EXISTS (SELECT FROM optima.company_links l WHERE l.company_id = c.id);
    IF v_company_id IS NOT NULL THEN
        INSERT INTO optima.company_links (company_id, database_name) VALUES (v_company_id, p_database_name);
        PERFORM platform.audit('optima_company_link', v_company_id::text, 'create', NULL,
            jsonb_build_object('databaseName', p_database_name, 'by', 'taxNumber'));
    END IF;
END;
$$;

--changeset hr:optima.linked_database runOnChange:true splitStatements:false
--comment: Baza Optimy spółki; brak powiązania → U0001 notLinked; NIP bazy inny niż spółki → U0003 taxNumberMismatch.
CREATE OR REPLACE FUNCTION optima.linked_database(p_company_id uuid)
    RETURNS text
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_database text;
    v_tax text;
    v_nip text;
BEGIN
    SELECT c.nip INTO v_nip FROM platform.companies c WHERE c.id = p_company_id;
    IF v_nip IS NULL THEN
        PERFORM platform.fail_validation('{"companyId": ["notFound"]}');
    END IF;
    SELECT l.database_name, d.tax_number INTO v_database, v_tax
    FROM optima.company_links l
    LEFT JOIN optima.databases d ON d.database_name = l.database_name
    WHERE l.company_id = p_company_id;
    IF v_database IS NULL THEN
        PERFORM platform.fail_validation('{"companyId": ["notLinked"]}');
    END IF;
    IF v_tax IS NOT NULL AND v_tax <> v_nip THEN
        RAISE EXCEPTION 'tax number mismatch' USING ERRCODE = 'U0003', HINT = 'taxNumberMismatch';
    END IF;
    RETURN v_database;
END;
$$;

--changeset hr:optima.export_items runOnChange:true splitStatements:false
--comment: Dodatki albo potrącenia spółki za miesiąc (składniki umów, które obowiązują choć jeden dzień miesiąca).
--comment: skip_reason: notFixed (procent / stawka × godziny — bez kwoty miesięcznej), notMapped (brak powiązania
--comment: z rodzajem płacy Optimy), payTypeInactive (rodzaj nieaktywny, wycofany albo usunięty w Optimie).
CREATE OR REPLACE FUNCTION optima.export_items(
    p_company_id uuid,
    p_database_name text,
    p_kind text,
    p_year integer,
    p_month integer
)
    RETURNS TABLE (
        item_id uuid,
        employee_id uuid,
        employee_code text,
        optima_id integer,
        amount numeric,
        skip_reason text
    )
    LANGUAGE sql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT ec.id, e.id, e.acronym, pt.optima_id, ec.value,
           CASE
               WHEN t.calculation <> 'fixed' THEN 'notFixed'
               WHEN pt.optima_id IS NULL THEN 'notMapped'
               WHEN NOT pt.is_active OR pt.is_withdrawn OR pt.missing_since IS NOT NULL THEN 'payTypeInactive'
           END
    FROM pay_components.employment_components ec
    JOIN pay_components.component_types t ON t.id = ec.component_type_id
    JOIN employment.employments m ON m.id = ec.employment_id
    JOIN employees.employees e ON e.id = m.employee_id
    LEFT JOIN optima.pay_types pt ON pt.database_name = p_database_name AND pt.component_type_id = t.id
    WHERE m.company_id = p_company_id
      AND t.kind = p_kind
      AND daterange(ec.valid_from, ec.valid_to, '[]')
          && daterange(make_date(p_year, p_month, 1), (make_date(p_year, p_month, 1) + interval '1 month')::date, '[)')
    ORDER BY e.acronym, ec.id;
$$;

--changeset hr:optima.current_employment runOnChange:true splitStatements:false
--comment: Umowa do eksportu kartoteki: najwcześniejsza niezakończona przed dniem p_on, a bez takiej — ostatnia.
CREATE OR REPLACE FUNCTION optima.current_employment(p_employee_id uuid, p_on date)
    RETURNS employment.employments
    LANGUAGE sql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT m.*
    FROM employment.employments m
    WHERE m.employee_id = p_employee_id
    ORDER BY (coalesce(m.terminated_at, m.end_date) IS NOT NULL AND coalesce(m.terminated_at, m.end_date) < p_on),
             CASE WHEN coalesce(m.terminated_at, m.end_date, p_on) >= p_on THEN m.start_date END,
             m.start_date DESC
    LIMIT 1;
$$;
