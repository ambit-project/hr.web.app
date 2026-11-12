--liquibase formatted sql

--changeset hr:platform_app.company_list runOnChange:true splitStatements:false
--comment: Lista spółek (Kadry, Administrator), wyszukiwanie po nazwie, nazwie krótkiej i NIP.
DROP FUNCTION IF EXISTS platform_app.company_list(text, boolean, integer, integer);
CREATE FUNCTION platform_app.company_list(
    p_search text DEFAULT NULL,
    p_include_inactive boolean DEFAULT false,
    p_limit integer DEFAULT 50,
    p_offset integer DEFAULT 0
)
    RETURNS TABLE (
        id uuid,
        short_name text,
        name text,
        nip text,
        regon text,
        street text,
        postal_code text,
        city text,
        is_active boolean,
        tax_office_id uuid,
        tax_office_code text,
        tax_office_name text,
        row_version bigint,
        total_count bigint
    )
    LANGUAGE plpgsql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_search text := nullif(btrim(p_search), '');
    v_like text := '%' || replace(replace(replace(v_search, '\', '\\'), '%', '\%'), '_', '\_') || '%';
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    RETURN QUERY
    SELECT c.id, c.short_name, c.name, c.nip, c.regon, c.street, c.postal_code, c.city, c.is_active,
           c.tax_office_id, t.code, t.name, c.xmin::text::bigint, count(*) OVER ()
    FROM platform.companies c
    LEFT JOIN dictionaries.tax_offices t ON t.id = c.tax_office_id
    WHERE (p_include_inactive OR c.is_active)
      AND (
          v_search IS NULL
          OR c.name ILIKE v_like
          OR c.short_name ILIKE v_like
          OR c.nip = platform.digits(v_search)
      )
    ORDER BY c.short_name COLLATE "pl-x-icu", c.id
    LIMIT least(greatest(p_limit, 1), 100)
    OFFSET greatest(p_offset, 0);
END;
$$;

--changeset hr:platform_app.company_create runOnChange:true splitStatements:false
--comment: Nowa spółka (Administrator), z audytem.
DROP PROCEDURE IF EXISTS platform_app.company_create(text, text, text, text, text, text, text, uuid);
CREATE OR REPLACE PROCEDURE platform_app.company_create(
    IN p_short_name text,
    IN p_name text,
    IN p_nip text,
    IN p_regon text,
    IN p_street text,
    IN p_postal_code text,
    IN p_city text,
    IN p_tax_office_id uuid,
    INOUT p_id uuid DEFAULT NULL
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_row platform.companies%ROWTYPE;
    v_errors jsonb;
BEGIN
    PERFORM platform.require_role('administrator');

    v_row.short_name := nullif(btrim(p_short_name), '');
    v_row.name := nullif(btrim(p_name), '');
    v_row.nip := platform.digits(p_nip);
    v_row.regon := platform.digits(p_regon);
    v_row.street := nullif(btrim(p_street), '');
    v_row.postal_code := nullif(btrim(p_postal_code), '');
    v_row.city := nullif(btrim(p_city), '');
    v_row.tax_office_id := p_tax_office_id;

    v_errors := platform.company_validate(NULL, v_row.short_name, v_row.name, v_row.nip, v_row.regon,
        v_row.street, v_row.postal_code, v_row.city, v_row.tax_office_id);
    IF v_errors <> '{}' THEN
        PERFORM platform.fail_validation(v_errors);
    END IF;

    INSERT INTO platform.companies (short_name, name, nip, regon, street, postal_code, city, tax_office_id,
                                    created_by)
    VALUES (v_row.short_name, v_row.name, v_row.nip, v_row.regon, v_row.street, v_row.postal_code, v_row.city,
            v_row.tax_office_id, platform.actor_id())
    RETURNING * INTO v_row;
    p_id := v_row.id;

    PERFORM platform.audit('company', p_id::text, 'create', NULL, to_jsonb(v_row));
END;
$$;

--changeset hr:platform_app.company_update runOnChange:true splitStatements:false
--comment: Edycja spółki (Administrator) z kontrolą wersji wiersza (U0003 przy nieaktualnej wersji).
DROP PROCEDURE IF EXISTS platform_app.company_update(uuid, bigint, text, text, text, text, text, text, text, boolean);
CREATE OR REPLACE PROCEDURE platform_app.company_update(
    IN p_id uuid,
    IN p_row_version bigint,
    IN p_short_name text,
    IN p_name text,
    IN p_nip text,
    IN p_regon text,
    IN p_street text,
    IN p_postal_code text,
    IN p_city text,
    IN p_tax_office_id uuid,
    IN p_is_active boolean
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_before platform.companies%ROWTYPE;
    v_after platform.companies%ROWTYPE;
    v_errors jsonb;
BEGIN
    PERFORM platform.require_role('administrator');

    SELECT * INTO v_before FROM platform.companies c WHERE c.id = p_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'company not found' USING ERRCODE = 'U0002';
    END IF;
    IF (SELECT c.xmin::text::bigint FROM platform.companies c WHERE c.id = p_id) <> p_row_version THEN
        RAISE EXCEPTION 'company changed' USING ERRCODE = 'U0003';
    END IF;

    v_after := v_before;
    v_after.short_name := nullif(btrim(p_short_name), '');
    v_after.name := nullif(btrim(p_name), '');
    v_after.nip := platform.digits(p_nip);
    v_after.regon := platform.digits(p_regon);
    v_after.street := nullif(btrim(p_street), '');
    v_after.postal_code := nullif(btrim(p_postal_code), '');
    v_after.city := nullif(btrim(p_city), '');
    v_after.tax_office_id := p_tax_office_id;
    v_after.is_active := coalesce(p_is_active, v_before.is_active);

    v_errors := platform.company_validate(p_id, v_after.short_name, v_after.name, v_after.nip, v_after.regon,
        v_after.street, v_after.postal_code, v_after.city, v_after.tax_office_id);
    IF v_errors <> '{}' THEN
        PERFORM platform.fail_validation(v_errors);
    END IF;

    UPDATE platform.companies c
    SET short_name = v_after.short_name, name = v_after.name, nip = v_after.nip, regon = v_after.regon,
        street = v_after.street, postal_code = v_after.postal_code, city = v_after.city,
        tax_office_id = v_after.tax_office_id, is_active = v_after.is_active
    WHERE c.id = p_id;

    PERFORM platform.audit('company', p_id::text, 'update', to_jsonb(v_before), to_jsonb(v_after));
END;
$$;
