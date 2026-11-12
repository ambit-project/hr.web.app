--liquibase formatted sql

--changeset hr:platform_app.legal_parameter_list runOnChange:true splitStatements:false
--comment: Parametry prawne (Administrator): filtr kodu i dnia obowiązywania.
CREATE OR REPLACE FUNCTION platform_app.legal_parameter_list(
    p_code text DEFAULT NULL,
    p_on date DEFAULT NULL,
    p_limit integer DEFAULT 50,
    p_offset integer DEFAULT 0
)
    RETURNS TABLE (
        id uuid,
        code text,
        value numeric,
        valid_from date,
        valid_to date,
        source text,
        total_count bigint
    )
    LANGUAGE plpgsql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_code text := nullif(btrim(p_code), '');
BEGIN
    PERFORM platform.require_role('administrator');
    RETURN QUERY
    SELECT p.id, p.code, p.value, p.valid_from, p.valid_to, p.source, count(*) OVER ()
    FROM platform.legal_parameters p
    WHERE (v_code IS NULL OR p.code LIKE replace(replace(lower(v_code), '%', ''), '_', '\_') || '%')
      AND (p_on IS NULL OR daterange(p.valid_from, p.valid_to, '[]') @> p_on)
    ORDER BY p.code, p.valid_from DESC
    LIMIT least(greatest(p_limit, 1), 100)
    OFFSET greatest(p_offset, 0);
END;
$$;

--changeset hr:platform_app.legal_parameter_add runOnChange:true splitStatements:false
--comment: Nowa wartość parametru od dnia (R19): zamyka poprzedni okres; ten sam dzień = poprawka wartości.
CREATE OR REPLACE PROCEDURE platform_app.legal_parameter_add(
    IN p_code text,
    IN p_value numeric,
    IN p_valid_from date,
    IN p_source text,
    INOUT p_id uuid DEFAULT NULL
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_code text := lower(nullif(btrim(p_code), ''));
    v_source text := nullif(btrim(p_source), '');
    v_last platform.legal_parameters%ROWTYPE;
    v_errors jsonb := '{}';
BEGIN
    PERFORM platform.require_role('administrator');

    IF v_code IS NULL THEN
        v_errors := v_errors || '{"code": ["required"]}';
    ELSIF v_code !~ '^[a-z0-9]+(\.[a-z0-9]+)*$' THEN
        v_errors := v_errors || '{"code": ["invalid"]}';
    END IF;
    IF p_value IS NULL THEN
        v_errors := v_errors || '{"value": ["required"]}';
    END IF;
    IF p_valid_from IS NULL THEN
        v_errors := v_errors || '{"validFrom": ["required"]}';
    END IF;
    IF v_source IS NULL THEN
        v_errors := v_errors || '{"source": ["required"]}';
    END IF;

    IF v_errors = '{}' THEN
        -- Jedno dodanie naraz dla kodu: dwa równoległe zamknięcia tego samego okresu by się nadpisały.
        PERFORM pg_advisory_xact_lock(hashtext('platform.legal_parameter:' || v_code));
        SELECT * INTO v_last
        FROM platform.legal_parameters p
        WHERE p.code = v_code
        ORDER BY p.valid_from DESC
        LIMIT 1;
        IF FOUND AND p_valid_from < v_last.valid_from THEN
            v_errors := v_errors || '{"validFrom": ["beforeCurrent"]}';
        END IF;
    END IF;

    IF v_errors <> '{}' THEN
        PERFORM platform.fail_validation(v_errors);
    END IF;

    IF v_last.id IS NOT NULL AND p_valid_from = v_last.valid_from THEN
        UPDATE platform.legal_parameters p SET value = p_value, source = v_source WHERE p.id = v_last.id;
        p_id := v_last.id;
        PERFORM platform.audit('legal_parameter', p_id::text, 'update', to_jsonb(v_last),
            jsonb_build_object('code', v_code, 'value', p_value, 'valid_from', p_valid_from, 'source', v_source));
        RETURN;
    END IF;

    IF v_last.id IS NOT NULL AND (v_last.valid_to IS NULL OR v_last.valid_to >= p_valid_from) THEN
        UPDATE platform.legal_parameters p SET valid_to = p_valid_from - 1 WHERE p.id = v_last.id;
        PERFORM platform.audit('legal_parameter', v_last.id::text, 'update', to_jsonb(v_last),
            jsonb_build_object('valid_to', p_valid_from - 1));
    END IF;

    INSERT INTO platform.legal_parameters (code, value, valid_from, source, created_by)
    VALUES (v_code, p_value, p_valid_from, v_source, platform.actor_id())
    RETURNING id INTO p_id;
    PERFORM platform.audit('legal_parameter', p_id::text, 'create', NULL,
        jsonb_build_object('code', v_code, 'value', p_value, 'valid_from', p_valid_from, 'source', v_source));
END;
$$;
