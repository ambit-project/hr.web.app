--liquibase formatted sql

--changeset hr:optima_app.agent_key_create runOnChange:true splitStatements:false
--comment: Nowy klucz agenta (Administrator). Token losuje API i pokazuje raz; baza dostaje tylko skrót i początek tokenu.
CREATE OR REPLACE PROCEDURE optima_app.agent_key_create(
    IN p_name text,
    IN p_key_hash bytea,
    IN p_key_prefix text,
    INOUT p_id uuid DEFAULT NULL
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_name text := nullif(btrim(p_name), '');
BEGIN
    PERFORM platform.require_role('administrator');
    IF v_name IS NULL THEN
        PERFORM platform.fail_validation('{"name": ["required"]}');
    ELSIF char_length(v_name) > 100 THEN
        PERFORM platform.fail_validation('{"name": ["maxLength"]}');
    END IF;

    INSERT INTO optima.agent_keys (name, key_hash, key_prefix, created_by)
    VALUES (v_name, p_key_hash, p_key_prefix, platform.actor_id())
    RETURNING id INTO p_id;
    PERFORM platform.audit('optima_agent_key', p_id::text, 'create', NULL,
        jsonb_build_object('name', v_name, 'keyPrefix', p_key_prefix));
END;
$$;

--changeset hr:optima_app.agent_key_list runOnChange:true splitStatements:false
--comment: Klucze agenta (Administrator), także odwołane; bez skrótów.
CREATE OR REPLACE FUNCTION optima_app.agent_key_list()
    RETURNS TABLE (
        id uuid,
        name text,
        key_prefix text,
        created_at timestamptz,
        revoked_at timestamptz,
        last_seen_at timestamptz,
        last_agent_version text
    )
    LANGUAGE plpgsql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    PERFORM platform.require_role('administrator');
    RETURN QUERY
    SELECT k.id, k.name, k.key_prefix, k.created_at, k.revoked_at, k.last_seen_at, k.last_agent_version
    FROM optima.agent_keys k
    ORDER BY k.revoked_at IS NOT NULL, k.created_at DESC;
END;
$$;

--changeset hr:optima_app.agent_key_revoke runOnChange:true splitStatements:false
--comment: Odwołanie klucza (Administrator); nieodwracalne, powtórzone nic nie zmienia.
CREATE OR REPLACE PROCEDURE optima_app.agent_key_revoke(IN p_id uuid)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    PERFORM platform.require_role('administrator');
    IF NOT EXISTS (SELECT FROM optima.agent_keys k WHERE k.id = p_id) THEN
        RAISE EXCEPTION 'agent key not found' USING ERRCODE = 'U0002';
    END IF;
    UPDATE optima.agent_keys SET revoked_at = now(), revoked_by = platform.actor_id()
    WHERE id = p_id AND revoked_at IS NULL;
    IF FOUND THEN
        PERFORM platform.audit('optima_agent_key', p_id::text, 'update', NULL,
            jsonb_build_object('revoked', true));
    END IF;
END;
$$;

--changeset hr:optima_app.agent_key_resolve runOnChange:true splitStatements:false
--comment: Uwierzytelnienie agenta: id aktywnego klucza po skrócie tokenu albo brak wiersza.
CREATE OR REPLACE FUNCTION optima_app.agent_key_resolve(p_key_hash bytea)
    RETURNS uuid
    LANGUAGE sql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT k.id FROM optima.agent_keys k WHERE k.key_hash = p_key_hash AND k.revoked_at IS NULL;
$$;
