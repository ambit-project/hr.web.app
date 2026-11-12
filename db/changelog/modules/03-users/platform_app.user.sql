--liquibase formatted sql

--changeset hr:platform.user_validate runOnChange:true splitStatements:false
--comment: Błędy pól konta jako JSON; p_id = konto edytowane (wyłączone ze sprawdzania duplikatów).
CREATE OR REPLACE FUNCTION platform.user_validate(
    p_id uuid,
    p_login text,
    p_email text,
    p_display_name text,
    p_roles text[]
)
    RETURNS jsonb
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_errors jsonb := '{}';
BEGIN
    IF p_login IS NULL THEN
        v_errors := v_errors || '{"login": ["required"]}';
    ELSIF p_login !~ '^[A-Za-z0-9._-]{3,100}$' THEN
        v_errors := v_errors || '{"login": ["invalid"]}';
    ELSIF EXISTS (SELECT FROM platform.users u WHERE lower(u.login) = lower(p_login) AND u.id IS DISTINCT FROM p_id) THEN
        v_errors := v_errors || '{"login": ["duplicate"]}';
    END IF;

    IF p_email IS NULL THEN
        v_errors := v_errors || '{"email": ["required"]}';
    ELSIF p_email !~ '^[^@\s]+@[^@\s]+\.[^@\s]+$' OR char_length(p_email) > 200 THEN
        v_errors := v_errors || '{"email": ["invalid"]}';
    ELSIF EXISTS (SELECT FROM platform.users u WHERE lower(u.email) = lower(p_email) AND u.id IS DISTINCT FROM p_id) THEN
        v_errors := v_errors || '{"email": ["duplicate"]}';
    END IF;

    IF p_display_name IS NULL THEN
        v_errors := v_errors || '{"displayName": ["required"]}';
    ELSIF char_length(p_display_name) > 200 THEN
        v_errors := v_errors || '{"displayName": ["maxLength"]}';
    END IF;

    IF coalesce(cardinality(p_roles), 0) = 0 THEN
        v_errors := v_errors || '{"roles": ["required"]}';
    ELSIF NOT p_roles <@ ARRAY['kadry', 'administrator', 'pracownik'] THEN
        v_errors := v_errors || '{"roles": ["invalid"]}';
    END IF;

    RETURN v_errors;
END;
$$;

--changeset hr:platform_app.user_list runOnChange:true splitStatements:false
--comment: Konta (Administrator).
CREATE OR REPLACE FUNCTION platform_app.user_list(
    p_search text DEFAULT NULL,
    p_limit integer DEFAULT 50,
    p_offset integer DEFAULT 0
)
    RETURNS TABLE (
        id uuid,
        login text,
        email text,
        display_name text,
        roles text[],
        is_active boolean,
        is_dev_account boolean,
        is_locked boolean,
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
    PERFORM platform.require_role('administrator');
    RETURN QUERY
    SELECT u.id, u.login, u.email, u.display_name,
           ARRAY(SELECT r.role FROM platform.user_roles r WHERE r.user_id = u.id ORDER BY r.role),
           u.is_active, u.is_dev_account, coalesce(u.locked_until > now(), false),
           u.xmin::text::bigint, count(*) OVER ()
    FROM platform.users u
    WHERE v_search IS NULL OR u.login ILIKE v_like OR u.email ILIKE v_like OR u.display_name ILIKE v_like
    ORDER BY u.display_name COLLATE "pl-x-icu", u.id
    LIMIT least(greatest(p_limit, 1), 100)
    OFFSET greatest(p_offset, 0);
END;
$$;

--changeset hr:platform_app.user_create runOnChange:true splitStatements:false
--comment: Nowe konto z hasłem początkowym (Administrator), R14.
CREATE OR REPLACE PROCEDURE platform_app.user_create(
    IN p_login text,
    IN p_email text,
    IN p_display_name text,
    IN p_roles text[],
    IN p_password text,
    IN p_min_password_length integer,
    INOUT p_id uuid DEFAULT NULL
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_login text := nullif(btrim(p_login), '');
    v_email text := nullif(btrim(p_email), '');
    v_display_name text := nullif(btrim(p_display_name), '');
    v_roles text[] := ARRAY(SELECT DISTINCT lower(r) FROM unnest(p_roles) r ORDER BY 1);
    v_errors jsonb;
BEGIN
    PERFORM platform.require_role('administrator');

    v_errors := platform.user_validate(NULL, v_login, v_email, v_display_name, v_roles);
    IF coalesce(char_length(p_password), 0) < p_min_password_length THEN
        v_errors := v_errors || '{"password": ["tooShort"]}';
    END IF;
    IF v_errors <> '{}' THEN
        PERFORM platform.fail_validation(v_errors);
    END IF;

    INSERT INTO platform.users (login, email, display_name, password_hash, created_by)
    VALUES (v_login, v_email, v_display_name, ext.crypt(p_password, ext.gen_salt('bf', 12)), platform.actor_id())
    RETURNING id INTO p_id;
    INSERT INTO platform.user_roles (user_id, role) SELECT p_id, r FROM unnest(v_roles) r;

    PERFORM platform.audit('user', p_id::text, 'create', NULL, jsonb_build_object(
        'login', v_login, 'email', v_email, 'display_name', v_display_name, 'roles', to_jsonb(v_roles)));
END;
$$;

--changeset hr:platform_app.user_update runOnChange:true splitStatements:false
--comment: Zmiana konta (Administrator). Nie można odebrać sobie roli Administratora ani wyłączyć siebie (U0003, R15).
CREATE OR REPLACE PROCEDURE platform_app.user_update(
    IN p_id uuid,
    IN p_row_version bigint,
    IN p_email text,
    IN p_display_name text,
    IN p_roles text[],
    IN p_is_active boolean
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_user platform.users%ROWTYPE;
    v_email text := nullif(btrim(p_email), '');
    v_display_name text := nullif(btrim(p_display_name), '');
    v_roles text[] := ARRAY(SELECT DISTINCT lower(r) FROM unnest(p_roles) r ORDER BY 1);
    v_before_roles text[];
    v_errors jsonb;
BEGIN
    PERFORM platform.require_role('administrator');

    SELECT * INTO v_user FROM platform.users u WHERE u.id = p_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'user not found' USING ERRCODE = 'U0002';
    END IF;
    IF (SELECT u.xmin::text::bigint FROM platform.users u WHERE u.id = p_id) <> p_row_version THEN
        RAISE EXCEPTION 'user changed' USING ERRCODE = 'U0003';
    END IF;
    IF p_id = platform.actor_id() AND (NOT p_is_active OR NOT 'administrator' = ANY (v_roles)) THEN
        RAISE EXCEPTION 'cannot remove own administrator access' USING ERRCODE = 'U0003',
            HINT = '{"reason": "selfLockout"}';
    END IF;

    v_errors := platform.user_validate(p_id, v_user.login, v_email, v_display_name, v_roles);
    IF v_errors <> '{}' THEN
        PERFORM platform.fail_validation(v_errors);
    END IF;

    v_before_roles := ARRAY(SELECT r.role FROM platform.user_roles r WHERE r.user_id = p_id ORDER BY r.role);
    UPDATE platform.users u
    SET email = v_email, display_name = v_display_name, is_active = p_is_active
    WHERE u.id = p_id;
    DELETE FROM platform.user_roles r WHERE r.user_id = p_id AND NOT r.role = ANY (v_roles);
    INSERT INTO platform.user_roles (user_id, role)
    SELECT p_id, r FROM unnest(v_roles) r
    ON CONFLICT DO NOTHING;
    IF NOT p_is_active THEN
        DELETE FROM platform.sessions s WHERE s.user_id = p_id;
    END IF;

    PERFORM platform.audit('user', p_id::text, 'update',
        jsonb_build_object('email', v_user.email, 'display_name', v_user.display_name,
            'roles', to_jsonb(v_before_roles), 'is_active', v_user.is_active),
        jsonb_build_object('email', v_email, 'display_name', v_display_name,
            'roles', to_jsonb(v_roles), 'is_active', p_is_active));
END;
$$;

--changeset hr:platform_app.user_set_password runOnChange:true splitStatements:false
--comment: Nowe hasło konta (Administrator): zdejmuje blokadę i kończy sesje konta. Hasło nie trafia do audytu.
CREATE OR REPLACE PROCEDURE platform_app.user_set_password(
    IN p_id uuid,
    IN p_password text,
    IN p_min_password_length integer
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    PERFORM platform.require_role('administrator');
    IF coalesce(char_length(p_password), 0) < p_min_password_length THEN
        PERFORM platform.fail_validation('{"password": ["tooShort"]}');
    END IF;

    UPDATE platform.users u
    SET password_hash = ext.crypt(p_password, ext.gen_salt('bf', 12)), failed_attempts = 0, locked_until = NULL
    WHERE u.id = p_id;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'user not found' USING ERRCODE = 'U0002';
    END IF;
    DELETE FROM platform.sessions s WHERE s.user_id = p_id;

    PERFORM platform.audit('user', p_id::text, 'update', NULL, '{"password": "changed"}');
END;
$$;
