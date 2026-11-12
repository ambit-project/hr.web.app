--liquibase formatted sql

--changeset hr:platform_app.audit_list runOnChange:true splitStatements:false
--comment: Przegląd audytu zmian (Administrator, R24): filtr encji, id, konta i okresu; najnowsze najpierw.
CREATE OR REPLACE FUNCTION platform_app.audit_list(
    p_entity text DEFAULT NULL,
    p_entity_id text DEFAULT NULL,
    p_actor_id uuid DEFAULT NULL,
    p_from date DEFAULT NULL,
    p_to date DEFAULT NULL,
    p_limit integer DEFAULT 50,
    p_offset integer DEFAULT 0
)
    RETURNS TABLE (
        id bigint,
        at timestamptz,
        actor_id uuid,
        actor_name text,
        entity text,
        entity_id text,
        action text,
        before jsonb,
        after jsonb,
        total_count bigint
    )
    LANGUAGE plpgsql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    PERFORM platform.require_role('administrator');
    RETURN QUERY
    SELECT a.id, a.at, a.actor_id, u.display_name, a.entity, a.entity_id, a.action, a.before, a.after,
           count(*) OVER ()
    FROM platform.audit_entries a
    LEFT JOIN platform.users u ON u.id = a.actor_id
    WHERE (nullif(p_entity, '') IS NULL OR a.entity = p_entity)
      AND (nullif(p_entity_id, '') IS NULL OR a.entity_id = p_entity_id)
      AND (p_actor_id IS NULL OR a.actor_id = p_actor_id)
      AND (p_from IS NULL OR a.at >= p_from::timestamp AT TIME ZONE 'Europe/Warsaw')
      AND (p_to IS NULL OR a.at < (p_to + 1)::timestamp AT TIME ZONE 'Europe/Warsaw')
    ORDER BY a.id DESC
    LIMIT least(greatest(p_limit, 1), 100)
    OFFSET greatest(p_offset, 0);
END;
$$;
