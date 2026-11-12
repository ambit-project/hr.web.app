--liquibase formatted sql

--changeset hr:dictionaries_app.position_create runOnChange:true splitStatements:false
--comment: Dodanie stanowiska (Kadry, Administrator), z audytem. Walidacja w bazie (U0001 z polami w HINT).
CREATE OR REPLACE PROCEDURE dictionaries_app.position_create(
    IN p_name text,
    IN p_description text,
    INOUT p_id uuid DEFAULT NULL
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_name text := btrim(p_name);
    v_description text := nullif(btrim(p_description), '');
    v_errors jsonb := '{}';
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');

    IF v_name IS NULL OR v_name = '' THEN
        v_errors := v_errors || jsonb_build_object('name', jsonb_build_array('required'));
    ELSIF char_length(v_name) > 200 THEN
        v_errors := v_errors || jsonb_build_object('name', jsonb_build_array('maxLength'));
    ELSIF EXISTS (SELECT FROM dictionaries.positions p WHERE lower(p.name) = lower(v_name)) THEN
        v_errors := v_errors || jsonb_build_object('name', jsonb_build_array('duplicate'));
    END IF;

    IF char_length(v_description) > 1000 THEN
        v_errors := v_errors || jsonb_build_object('description', jsonb_build_array('maxLength'));
    END IF;

    IF v_errors <> '{}' THEN
        PERFORM platform.fail_validation(v_errors);
    END IF;

    INSERT INTO dictionaries.positions (name, description, created_by)
    VALUES (v_name, v_description, platform.actor_id())
    RETURNING id INTO p_id;

    PERFORM platform.audit('position', p_id::text, 'create', NULL,
        jsonb_build_object('name', v_name, 'description', v_description));
EXCEPTION
    WHEN unique_violation THEN
        PERFORM platform.fail_validation('{"name": ["duplicate"]}');
END;
$$;
