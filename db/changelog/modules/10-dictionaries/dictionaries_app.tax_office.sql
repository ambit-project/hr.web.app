--liquibase formatted sql

--changeset hr:dictionaries_app.tax_office_list runOnChange:true splitStatements:false
--comment: Lista urzędów skarbowych (Kadry, Administrator): wyszukiwanie po początku kodu i fragmencie nazwy.
CREATE OR REPLACE FUNCTION dictionaries_app.tax_office_list(
    p_search text DEFAULT NULL,
    p_include_inactive boolean DEFAULT false,
    p_limit integer DEFAULT 50,
    p_offset integer DEFAULT 0
)
    RETURNS TABLE (
        id uuid,
        code text,
        name text,
        is_active boolean,
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
    SELECT t.id, t.code, t.name, t.is_active, t.xmin::text::bigint, count(*) OVER ()
    FROM dictionaries.tax_offices t
    WHERE (p_include_inactive OR t.is_active)
      AND (v_search IS NULL OR t.code LIKE v_search || '%' OR t.name ILIKE v_like)
    ORDER BY t.code, t.id
    LIMIT least(greatest(p_limit, 1), 100)
    OFFSET greatest(p_offset, 0);
END;
$$;

--changeset hr:dictionaries_app.tax_office_create runOnChange:true splitStatements:false
--comment: Nowy urząd skarbowy (Administrator), z audytem.
CREATE OR REPLACE PROCEDURE dictionaries_app.tax_office_create(
    IN p_code text,
    IN p_name text,
    INOUT p_id uuid DEFAULT NULL
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_row dictionaries.tax_offices%ROWTYPE;
    v_errors jsonb := '{}';
BEGIN
    PERFORM platform.require_role('administrator');

    v_row.code := nullif(btrim(p_code), '');
    v_row.name := nullif(btrim(p_name), '');

    IF v_row.code IS NULL THEN
        v_errors := v_errors || '{"code": ["required"]}';
    ELSIF v_row.code !~ '^\d{4}$' THEN
        v_errors := v_errors || '{"code": ["invalid"]}';
    ELSIF EXISTS (SELECT FROM dictionaries.tax_offices t WHERE t.code = v_row.code) THEN
        v_errors := v_errors || '{"code": ["duplicate"]}';
    END IF;
    IF v_row.name IS NULL THEN
        v_errors := v_errors || '{"name": ["required"]}';
    ELSIF char_length(v_row.name) > 300 THEN
        v_errors := v_errors || '{"name": ["maxLength"]}';
    END IF;
    IF v_errors <> '{}' THEN
        PERFORM platform.fail_validation(v_errors);
    END IF;

    INSERT INTO dictionaries.tax_offices (code, name, created_by)
    VALUES (v_row.code, v_row.name, platform.actor_id())
    RETURNING * INTO v_row;
    p_id := v_row.id;

    PERFORM platform.audit('tax_office', p_id::text, 'create', NULL, to_jsonb(v_row));
EXCEPTION
    WHEN unique_violation THEN
        PERFORM platform.fail_validation('{"code": ["duplicate"]}');
END;
$$;

--changeset hr:dictionaries_app.tax_office_update runOnChange:true splitStatements:false
--comment: Edycja nazwy i aktywności urzędu (Administrator); kod jest niezmienny.
CREATE OR REPLACE PROCEDURE dictionaries_app.tax_office_update(
    IN p_id uuid,
    IN p_row_version bigint,
    IN p_name text,
    IN p_is_active boolean
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_before dictionaries.tax_offices%ROWTYPE;
    v_after dictionaries.tax_offices%ROWTYPE;
BEGIN
    PERFORM platform.require_role('administrator');

    SELECT * INTO v_before FROM dictionaries.tax_offices t WHERE t.id = p_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'tax office not found' USING ERRCODE = 'U0002';
    END IF;
    IF (SELECT t.xmin::text::bigint FROM dictionaries.tax_offices t WHERE t.id = p_id) <> p_row_version THEN
        RAISE EXCEPTION 'tax office changed' USING ERRCODE = 'U0003';
    END IF;

    v_after := v_before;
    v_after.name := nullif(btrim(p_name), '');
    v_after.is_active := coalesce(p_is_active, v_before.is_active);

    IF v_after.name IS NULL THEN
        PERFORM platform.fail_validation('{"name": ["required"]}');
    ELSIF char_length(v_after.name) > 300 THEN
        PERFORM platform.fail_validation('{"name": ["maxLength"]}');
    END IF;

    UPDATE dictionaries.tax_offices t SET name = v_after.name, is_active = v_after.is_active WHERE t.id = p_id;

    PERFORM platform.audit('tax_office', p_id::text, 'update', to_jsonb(v_before), to_jsonb(v_after));
END;
$$;
