--liquibase formatted sql

--changeset hr:dictionaries.zus_code_validate runOnChange:true splitStatements:false
--comment: Błędy pól kodu ZUS (nazwa i okres); format kodu sprawdza zus_code_create.
CREATE OR REPLACE FUNCTION dictionaries.zus_code_validate(
    p_name text,
    p_valid_from date,
    p_valid_to date
)
    RETURNS jsonb
    LANGUAGE plpgsql
    IMMUTABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_errors jsonb := '{}';
BEGIN
    IF p_name IS NULL THEN
        v_errors := v_errors || '{"name": ["required"]}';
    ELSIF char_length(p_name) > 2000 THEN
        v_errors := v_errors || '{"name": ["maxLength"]}';
    END IF;
    IF p_valid_from IS NULL THEN
        v_errors := v_errors || '{"validFrom": ["required"]}';
    ELSIF p_valid_to < p_valid_from THEN
        v_errors := v_errors || '{"validTo": ["beforeFrom"]}';
    END IF;
    RETURN v_errors;
END;
$$;

--changeset hr:dictionaries.zus_code_valid runOnChange:true splitStatements:false
--comment: Czy kod danego rodzaju obowiązuje w dniu (dla przypisania kodów w zatrudnieniu, moduł 004).
CREATE OR REPLACE FUNCTION dictionaries.zus_code_valid(p_kind text, p_code text, p_on date)
    RETURNS boolean
    LANGUAGE sql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT EXISTS (
        SELECT FROM dictionaries.zus_codes z
        WHERE z.kind = p_kind AND z.code = p_code
          AND z.valid_from <= p_on AND (z.valid_to IS NULL OR z.valid_to >= p_on)
    );
$$;

--changeset hr:dictionaries_app.zus_code_list runOnChange:true splitStatements:false
--comment: Lista kodów ZUS (Kadry, Administrator): filtr rodzaju, wyszukiwanie po kodzie i nazwie, obowiązujące w dniu.
CREATE OR REPLACE FUNCTION dictionaries_app.zus_code_list(
    p_kind text DEFAULT NULL,
    p_search text DEFAULT NULL,
    p_on_date date DEFAULT NULL,
    p_limit integer DEFAULT 50,
    p_offset integer DEFAULT 0
)
    RETURNS TABLE (
        id uuid,
        kind text,
        code text,
        name text,
        valid_from date,
        valid_to date,
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
    SELECT z.id, z.kind, z.code, z.name, z.valid_from, z.valid_to, z.xmin::text::bigint, count(*) OVER ()
    FROM dictionaries.zus_codes z
    WHERE (p_kind IS NULL OR z.kind = p_kind)
      AND (v_search IS NULL OR z.code LIKE v_search || '%' OR z.name ILIKE v_like)
      AND (p_on_date IS NULL OR (z.valid_from <= p_on_date AND (z.valid_to IS NULL OR z.valid_to >= p_on_date)))
    ORDER BY array_position(ARRAY['title', 'pension', 'disability'], z.kind), z.code, z.id
    LIMIT least(greatest(p_limit, 1), 100)
    OFFSET greatest(p_offset, 0);
END;
$$;

--changeset hr:dictionaries_app.zus_code_create runOnChange:true splitStatements:false
--comment: Nowy kod ZUS (Administrator), z audytem.
CREATE OR REPLACE PROCEDURE dictionaries_app.zus_code_create(
    IN p_kind text,
    IN p_code text,
    IN p_name text,
    IN p_valid_from date,
    IN p_valid_to date,
    INOUT p_id uuid DEFAULT NULL
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_row dictionaries.zus_codes%ROWTYPE;
    v_errors jsonb;
BEGIN
    PERFORM platform.require_role('administrator');

    v_row.kind := nullif(btrim(p_kind), '');
    v_row.code := nullif(btrim(p_code), '');
    v_row.name := nullif(btrim(p_name), '');
    v_row.valid_from := p_valid_from;
    v_row.valid_to := p_valid_to;

    v_errors := dictionaries.zus_code_validate(v_row.name, v_row.valid_from, v_row.valid_to);
    IF v_row.kind IS NULL THEN
        v_errors := v_errors || '{"kind": ["required"]}';
    ELSIF v_row.kind NOT IN ('title', 'pension', 'disability') THEN
        v_errors := v_errors || '{"kind": ["invalid"]}';
    END IF;
    IF v_row.code IS NULL THEN
        v_errors := v_errors || '{"code": ["required"]}';
    ELSIF v_row.kind = 'title' AND v_row.code !~ '^\d{4}$'
       OR v_row.kind IN ('pension', 'disability') AND v_row.code !~ '^\d$' THEN
        v_errors := v_errors || '{"code": ["invalid"]}';
    ELSIF EXISTS (SELECT FROM dictionaries.zus_codes z WHERE z.kind = v_row.kind AND z.code = v_row.code) THEN
        v_errors := v_errors || '{"code": ["duplicate"]}';
    END IF;
    IF v_errors <> '{}' THEN
        PERFORM platform.fail_validation(v_errors);
    END IF;

    INSERT INTO dictionaries.zus_codes (kind, code, name, valid_from, valid_to, created_by)
    VALUES (v_row.kind, v_row.code, v_row.name, v_row.valid_from, v_row.valid_to, platform.actor_id())
    RETURNING * INTO v_row;
    p_id := v_row.id;

    PERFORM platform.audit('zus_code', p_id::text, 'create', NULL, to_jsonb(v_row));
EXCEPTION
    WHEN unique_violation THEN
        PERFORM platform.fail_validation('{"code": ["duplicate"]}');
END;
$$;

--changeset hr:dictionaries_app.zus_code_update runOnChange:true splitStatements:false
--comment: Edycja nazwy i okresu kodu ZUS (Administrator); rodzaj i kod są niezmienne.
CREATE OR REPLACE PROCEDURE dictionaries_app.zus_code_update(
    IN p_id uuid,
    IN p_row_version bigint,
    IN p_name text,
    IN p_valid_from date,
    IN p_valid_to date
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_before dictionaries.zus_codes%ROWTYPE;
    v_after dictionaries.zus_codes%ROWTYPE;
    v_errors jsonb;
BEGIN
    PERFORM platform.require_role('administrator');

    SELECT * INTO v_before FROM dictionaries.zus_codes z WHERE z.id = p_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'zus code not found' USING ERRCODE = 'U0002';
    END IF;
    IF (SELECT z.xmin::text::bigint FROM dictionaries.zus_codes z WHERE z.id = p_id) <> p_row_version THEN
        RAISE EXCEPTION 'zus code changed' USING ERRCODE = 'U0003';
    END IF;

    v_after := v_before;
    v_after.name := nullif(btrim(p_name), '');
    v_after.valid_from := p_valid_from;
    v_after.valid_to := p_valid_to;

    v_errors := dictionaries.zus_code_validate(v_after.name, v_after.valid_from, v_after.valid_to);
    IF v_errors <> '{}' THEN
        PERFORM platform.fail_validation(v_errors);
    END IF;

    UPDATE dictionaries.zus_codes z
    SET name = v_after.name, valid_from = v_after.valid_from, valid_to = v_after.valid_to
    WHERE z.id = p_id;

    PERFORM platform.audit('zus_code', p_id::text, 'update', to_jsonb(v_before), to_jsonb(v_after));
END;
$$;
