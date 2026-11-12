--liquibase formatted sql

--changeset hr:platform_app.session_login runOnChange:true splitStatements:false
--comment: Logowanie (R1-R5). Porażka zwraca NULL zamiast błędu, bo licznik prób musi się zapisać.
CREATE OR REPLACE PROCEDURE platform_app.session_login(
    IN p_login text,
    IN p_password text,
    IN p_token text,
    IN p_hours integer,
    IN p_max_failed integer,
    IN p_lockout_minutes integer,
    INOUT p_user_id uuid DEFAULT NULL
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_user platform.users%ROWTYPE;
    v_failed integer;
BEGIN
    p_user_id := NULL;

    SELECT * INTO v_user
    FROM platform.users u
    WHERE lower(u.login) = lower(btrim(p_login)) OR lower(u.email) = lower(btrim(p_login))
    FOR UPDATE;

    IF NOT FOUND OR NOT v_user.is_active OR v_user.password_hash IS NULL THEN
        RETURN;
    END IF;

    IF v_user.locked_until > now() THEN
        RETURN;
    END IF;

    -- Blokada minęła: liczymy próby od nowa.
    v_failed := CASE WHEN v_user.locked_until IS NOT NULL THEN 0 ELSE v_user.failed_attempts END;

    IF ext.crypt(p_password, v_user.password_hash) <> v_user.password_hash THEN
        v_failed := v_failed + 1;
        UPDATE platform.users
        SET failed_attempts = v_failed,
            locked_until = CASE
                WHEN v_failed >= p_max_failed THEN now() + make_interval(mins => p_lockout_minutes)
            END
        WHERE id = v_user.id;
        RETURN;
    END IF;

    UPDATE platform.users SET failed_attempts = 0, locked_until = NULL WHERE id = v_user.id;
    DELETE FROM platform.sessions WHERE user_id = v_user.id AND expires_at <= now();
    INSERT INTO platform.sessions (user_id, token_hash, expires_at)
    VALUES (v_user.id, ext.digest(p_token, 'sha256'), now() + make_interval(hours => p_hours));
    p_user_id := v_user.id;
END;
$$;

--changeset hr:platform_app.session_dev_login runOnChange:true splitStatements:false
--comment: Logowanie deweloperskie (R8-R9): tylko konta is_dev_account, które istnieją wyłącznie w context dev.
CREATE OR REPLACE PROCEDURE platform_app.session_dev_login(
    IN p_role text,
    IN p_token text,
    IN p_hours integer,
    INOUT p_user_id uuid DEFAULT NULL
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    SELECT u.id INTO p_user_id
    FROM platform.users u
    JOIN platform.user_roles r ON r.user_id = u.id
    WHERE u.is_dev_account AND u.is_active AND r.role = p_role
    ORDER BY u.login
    LIMIT 1;

    IF p_user_id IS NOT NULL THEN
        INSERT INTO platform.sessions (user_id, token_hash, expires_at)
        VALUES (p_user_id, ext.digest(p_token, 'sha256'), now() + make_interval(hours => p_hours));
    END IF;
END;
$$;

--changeset hr:platform_app.session_resolve runOnChange:true splitStatements:false
--comment: Konto i role dla tokenu ważnej sesji aktywnego konta (R2, R5, R6).
CREATE OR REPLACE FUNCTION platform_app.session_resolve(p_token text)
    RETURNS TABLE (user_id uuid, roles text[])
    LANGUAGE sql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT s.user_id,
           ARRAY(SELECT r.role FROM platform.user_roles r WHERE r.user_id = s.user_id ORDER BY r.role)
    FROM platform.sessions s
    JOIN platform.users u ON u.id = s.user_id
    WHERE s.token_hash = ext.digest(p_token, 'sha256')
      AND s.expires_at > now()
      AND u.is_active;
$$;

--changeset hr:platform_app.session_logout runOnChange:true splitStatements:false
CREATE OR REPLACE PROCEDURE platform_app.session_logout(IN p_token text)
    LANGUAGE sql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
    DELETE FROM platform.sessions WHERE token_hash = ext.digest(p_token, 'sha256');
$$;

--changeset hr:platform_app.me runOnChange:true splitStatements:false
--comment: Zalogowane konto (R7).
CREATE OR REPLACE FUNCTION platform_app.me()
    RETURNS TABLE (id uuid, login text, email text, display_name text, roles text[])
    LANGUAGE sql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT u.id, u.login, u.email, u.display_name,
           ARRAY(SELECT r.role FROM platform.user_roles r WHERE r.user_id = u.id ORDER BY r.role)
    FROM platform.users u
    WHERE u.id = platform.actor_id() AND u.is_active;
$$;

--changeset hr:platform_app.bootstrap_admin runOnChange:true splitStatements:false
--comment: Pierwszy Administrator przy starcie API (R10); nic nie robi, gdy Administrator istnieje.
CREATE OR REPLACE PROCEDURE platform_app.bootstrap_admin(
    IN p_login text,
    IN p_email text,
    IN p_display_name text,
    IN p_password text,
    INOUT p_created boolean DEFAULT false
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_id uuid;
BEGIN
    p_created := false;
    -- Jedna instancja API naraz zakłada administratora.
    PERFORM pg_advisory_xact_lock(hashtext('platform.bootstrap_admin'));
    IF EXISTS (
        SELECT FROM platform.user_roles r JOIN platform.users u ON u.id = r.user_id
        WHERE r.role = 'administrator' AND NOT u.is_dev_account
    ) THEN
        RETURN;
    END IF;

    INSERT INTO platform.users (login, email, display_name, password_hash)
    VALUES (btrim(p_login), btrim(p_email), btrim(p_display_name), ext.crypt(p_password, ext.gen_salt('bf', 12)))
    RETURNING id INTO v_id;
    INSERT INTO platform.user_roles (user_id, role) VALUES (v_id, 'administrator');
    PERFORM platform.audit('user', v_id::text, 'create', NULL,
        jsonb_build_object('login', p_login, 'email', p_email, 'roles', jsonb_build_array('administrator')));
    p_created := true;
END;
$$;

--changeset hr:platform_app.my_profiles runOnChange:true splitStatements:false
--comment: Profile zalogowanego konta: kartoteki powiązane z kontem, po jednej na spółkę (R13 specyfikacji 003).
CREATE OR REPLACE FUNCTION platform_app.my_profiles()
    RETURNS TABLE (employee_id uuid, company_id uuid, company_short_name text)
    LANGUAGE sql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT l.employee_id, l.company_id, c.short_name
    FROM platform.user_employee_links l
    JOIN platform.companies c ON c.id = l.company_id
    JOIN platform.users u ON u.id = l.user_id AND u.is_active
    WHERE l.user_id = platform.actor_id()
    ORDER BY c.short_name COLLATE "pl-x-icu";
$$;
