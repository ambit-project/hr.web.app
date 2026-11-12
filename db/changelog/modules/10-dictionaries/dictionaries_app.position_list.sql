--liquibase formatted sql

--changeset hr:dictionaries_app.position_list runOnChange:true splitStatements:false
--comment: Lista stanowisk (Kadry, Administrator): wyszukiwanie po nazwie, stronicowanie, total_count w każdym wierszu.
CREATE OR REPLACE FUNCTION dictionaries_app.position_list(
    p_search text DEFAULT NULL,
    p_include_inactive boolean DEFAULT false,
    p_limit integer DEFAULT 50,
    p_offset integer DEFAULT 0
)
    RETURNS TABLE (
        id uuid,
        name text,
        description text,
        is_active boolean,
        created_at timestamptz,
        total_count bigint
    )
    LANGUAGE plpgsql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    RETURN QUERY
    SELECT p.id, p.name, p.description, p.is_active, p.created_at, count(*) OVER ()
    FROM dictionaries.positions p
    WHERE (p_include_inactive OR p.is_active)
      AND (
          nullif(btrim(p_search), '') IS NULL
          OR p.name ILIKE '%' || replace(replace(replace(btrim(p_search), '\', '\\'), '%', '\%'), '_', '\_') || '%'
      )
    ORDER BY p.name COLLATE "pl-x-icu", p.id
    LIMIT least(greatest(p_limit, 1), 100)
    OFFSET greatest(p_offset, 0);
END;
$$;
