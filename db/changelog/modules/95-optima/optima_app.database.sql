--liquibase formatted sql

--changeset hr:optima_app.database_list runOnChange:true splitStatements:false
--comment: Spółki i bazy firm Optimy: każda aktywna spółka i każda zgłoszona baza, z powiązaniem i zgodnością NIP.
DROP FUNCTION IF EXISTS optima_app.database_list();
CREATE FUNCTION optima_app.database_list()
    RETURNS TABLE (
        company_id uuid,
        company_short_name text,
        company_nip text,
        database_name text,
        tax_number text,
        last_seen_at timestamptz,
        tax_number_matches boolean
    )
    LANGUAGE plpgsql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    RETURN QUERY
    SELECT c.id, c.short_name, c.nip, coalesce(l.database_name, d.database_name), d.tax_number, d.last_seen_at,
           CASE WHEN c.id IS NULL OR d.tax_number IS NULL THEN NULL ELSE d.tax_number = c.nip END
    FROM (SELECT * FROM platform.companies WHERE is_active) c
    LEFT JOIN optima.company_links l ON l.company_id = c.id
    FULL JOIN optima.databases d ON d.database_name = l.database_name
    ORDER BY c.short_name COLLATE "pl-x-icu" NULLS LAST, d.database_name;
END;
$$;

--changeset hr:optima_app.company_link_set runOnChange:true splitStatements:false
--comment: Powiązanie spółki z bazą firmy Optimy (Administrator); NULL usuwa powiązanie. Baza zgłoszona z innym NIP → U0001.
CREATE OR REPLACE PROCEDURE optima_app.company_link_set(IN p_company_id uuid, IN p_database_name text)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_database text := nullif(btrim(p_database_name), '');
    v_nip text;
    v_before text;
BEGIN
    PERFORM platform.require_role('administrator');
    SELECT c.nip INTO v_nip FROM platform.companies c WHERE c.id = p_company_id;
    IF v_nip IS NULL THEN
        RAISE EXCEPTION 'company not found' USING ERRCODE = 'U0002';
    END IF;
    SELECT l.database_name INTO v_before FROM optima.company_links l WHERE l.company_id = p_company_id;

    IF v_database IS NULL THEN
        DELETE FROM optima.company_links WHERE company_id = p_company_id;
    ELSE
        IF v_database !~ '^[^\[\]\x00-\x1F]{1,128}$' THEN
            PERFORM platform.fail_validation('{"databaseName": ["invalid"]}');
        END IF;
        IF EXISTS (SELECT FROM optima.company_links l
                   WHERE l.database_name = v_database AND l.company_id <> p_company_id) THEN
            PERFORM platform.fail_validation('{"databaseName": ["duplicate"]}');
        END IF;
        IF EXISTS (SELECT FROM optima.databases d
                   WHERE d.database_name = v_database AND d.tax_number IS NOT NULL AND d.tax_number <> v_nip) THEN
            PERFORM platform.fail_validation('{"databaseName": ["taxNumberMismatch"]}');
        END IF;
        INSERT INTO optima.company_links (company_id, database_name, linked_by)
        VALUES (p_company_id, v_database, platform.actor_id())
        ON CONFLICT (company_id) DO UPDATE
            SET database_name = excluded.database_name, linked_at = now(), linked_by = excluded.linked_by;
    END IF;

    IF v_before IS DISTINCT FROM v_database THEN
        PERFORM platform.audit('optima_company_link', p_company_id::text, 'update',
            jsonb_build_object('databaseName', v_before), jsonb_build_object('databaseName', v_database));
    END IF;
END;
$$;

--changeset hr:optima_app.pay_type_list runOnChange:true splitStatements:false
--comment: Rodzaje płac z bazy Optimy spółki z powiązaniem do słownika składników (Kadry, Administrator).
DROP FUNCTION IF EXISTS optima_app.pay_type_list(uuid);
CREATE FUNCTION optima_app.pay_type_list(p_company_id uuid)
    RETURNS TABLE (
        optima_id integer,
        name text,
        short_name text,
        is_deduction boolean,
        source_kind integer,
        standard_id integer,
        default_amount numeric,
        is_pkzp boolean,
        is_seizure boolean,
        is_active boolean,
        is_withdrawn boolean,
        imported_at timestamptz,
        missing_since timestamptz,
        component_type_id uuid,
        component_code text,
        component_name text
    )
    LANGUAGE plpgsql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_database text;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    IF NOT EXISTS (SELECT FROM platform.companies c WHERE c.id = p_company_id) THEN
        RAISE EXCEPTION 'company not found' USING ERRCODE = 'U0002';
    END IF;
    SELECT l.database_name INTO v_database FROM optima.company_links l WHERE l.company_id = p_company_id;
    RETURN QUERY
    SELECT p.optima_id, p.name, p.short_name, p.is_deduction, p.source_kind, p.standard_id, p.default_amount,
           p.is_pkzp, p.is_seizure, p.is_active, p.is_withdrawn, p.imported_at, p.missing_since,
           t.id, t.code, t.name
    FROM optima.pay_types p
    LEFT JOIN pay_components.component_types t ON t.id = p.component_type_id
    WHERE p.database_name = v_database
    ORDER BY p.is_deduction, p.name COLLATE "pl-x-icu", p.optima_id;
END;
$$;

--changeset hr:optima_app.pay_type_map runOnChange:true splitStatements:false
--comment: Powiązanie rodzaju płacy Optimy ze składnikiem hr.web.app (Kadry, Administrator); NULL usuwa powiązanie.
--comment: Dodatek tylko z dodatkiem, potrącenie z potrąceniem (kindMismatch); jeden składnik na rodzaj w bazie (duplicate).
CREATE OR REPLACE PROCEDURE optima_app.pay_type_map(
    IN p_company_id uuid,
    IN p_optima_id integer,
    IN p_component_type_id uuid
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_database text;
    v_pay_type optima.pay_types;
    v_component pay_components.component_types;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    SELECT l.database_name INTO v_database FROM optima.company_links l WHERE l.company_id = p_company_id;
    SELECT * INTO v_pay_type FROM optima.pay_types p
    WHERE p.database_name = v_database AND p.optima_id = p_optima_id
    FOR UPDATE;
    IF v_pay_type.optima_id IS NULL THEN
        RAISE EXCEPTION 'pay type not found' USING ERRCODE = 'U0002';
    END IF;

    IF p_component_type_id IS NOT NULL THEN
        SELECT * INTO v_component FROM pay_components.component_types t WHERE t.id = p_component_type_id;
        IF v_component.id IS NULL THEN
            PERFORM platform.fail_validation('{"componentTypeId": ["notFound"]}');
        ELSIF NOT v_component.is_active THEN
            PERFORM platform.fail_validation('{"componentTypeId": ["inactive"]}');
        ELSIF (v_component.kind = 'deduction') <> v_pay_type.is_deduction THEN
            PERFORM platform.fail_validation('{"componentTypeId": ["kindMismatch"]}');
        ELSIF EXISTS (SELECT FROM optima.pay_types p
                      WHERE p.database_name = v_database AND p.component_type_id = p_component_type_id
                        AND p.optima_id <> p_optima_id) THEN
            PERFORM platform.fail_validation('{"componentTypeId": ["duplicate"]}');
        END IF;
    END IF;

    IF v_pay_type.component_type_id IS DISTINCT FROM p_component_type_id THEN
        UPDATE optima.pay_types SET component_type_id = p_component_type_id
        WHERE database_name = v_database AND optima_id = p_optima_id;
        PERFORM platform.audit('optima_pay_type', v_database || '/' || p_optima_id, 'update',
            jsonb_build_object('componentTypeId', v_pay_type.component_type_id),
            jsonb_build_object('componentTypeId', p_component_type_id));
    END IF;
END;
$$;
