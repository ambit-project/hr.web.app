--liquibase formatted sql

--changeset hr:platform.actor_id runOnChange:true splitStatements:false
--comment: Kto wykonuje operację (ustawienie sesji hr.actor_id, ustawia API).
CREATE OR REPLACE FUNCTION platform.actor_id()
    RETURNS uuid
    LANGUAGE sql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT nullif(current_setting('hr.actor_id', true), '')::uuid;
$$;
