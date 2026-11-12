--liquibase formatted sql

--changeset hr:platform.audit runOnChange:true splitStatements:false
--comment: Wpis audytu zmian (R22); wołany przez rutyny zapisu w tej samej transakcji.
CREATE OR REPLACE FUNCTION platform.audit(
    p_entity text,
    p_entity_id text,
    p_action text,
    p_before jsonb,
    p_after jsonb
)
    RETURNS void
    LANGUAGE sql
    SET search_path = pg_catalog, pg_temp
AS $$
    INSERT INTO platform.audit_entries (actor_id, entity, entity_id, action, before, after)
    VALUES (platform.actor_id(), p_entity, p_entity_id, p_action, p_before, p_after);
$$;

--changeset hr:platform.read_audit runOnChange:true splitStatements:false
--comment: Wpis audytu odczytu (R23): zapis tylko, gdy czyta ktoś inny niż sam pracownik (moduły od 003).
CREATE OR REPLACE FUNCTION platform.read_audit(p_employee_id uuid, p_resource text, p_resource_id text)
    RETURNS void
    LANGUAGE sql
    SET search_path = pg_catalog, pg_temp
AS $$
    INSERT INTO platform.read_audit_entries (actor_id, employee_id, resource, resource_id)
    SELECT platform.actor_id(), p_employee_id, p_resource, p_resource_id
    WHERE platform.actor_id() IS NOT NULL;
$$;
