--liquibase formatted sql

--changeset hr:platform.require_role runOnChange:true splitStatements:false
--comment: Aktor musi mieć jedną z ról; inaczej U0004 (403).
CREATE OR REPLACE FUNCTION platform.require_role(VARIADIC p_roles text[])
    RETURNS void
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    IF NOT EXISTS (
        SELECT FROM platform.user_roles r
        JOIN platform.users u ON u.id = r.user_id
        WHERE r.user_id = platform.actor_id() AND u.is_active AND r.role = ANY (p_roles)
    ) THEN
        RAISE EXCEPTION 'forbidden' USING ERRCODE = 'U0004';
    END IF;
END;
$$;
