--liquibase formatted sql

--changeset hr:dictionaries_app.position_update runOnChange:true splitStatements:false
--comment: Edycja stanowiska (Kadry, Administrator) z kontrolą wersji wiersza i audytem.
CREATE OR REPLACE PROCEDURE dictionaries_app.position_update(
    IN p_id uuid,
    IN p_row_version bigint,
    IN p_name text,
    IN p_description text,
    IN p_is_active boolean
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_before dictionaries.positions%ROWTYPE;
    v_after dictionaries.positions%ROWTYPE;
    v_errors jsonb := '{}';
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');

    SELECT * INTO v_before FROM dictionaries.positions p WHERE p.id = p_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'position not found' USING ERRCODE = 'U0002';
    END IF;
    IF (SELECT p.xmin::text::bigint FROM dictionaries.positions p WHERE p.id = p_id) <> p_row_version THEN
        RAISE EXCEPTION 'position changed' USING ERRCODE = 'U0003';
    END IF;

    v_after := v_before;
    v_after.name := nullif(btrim(p_name), '');
    v_after.description := nullif(btrim(p_description), '');
    v_after.is_active := coalesce(p_is_active, v_before.is_active);

    IF v_after.name IS NULL THEN
        v_errors := v_errors || '{"name": ["required"]}';
    ELSIF char_length(v_after.name) > 200 THEN
        v_errors := v_errors || '{"name": ["maxLength"]}';
    ELSIF EXISTS (
        SELECT FROM dictionaries.positions p WHERE lower(p.name) = lower(v_after.name) AND p.id <> p_id
    ) THEN
        v_errors := v_errors || '{"name": ["duplicate"]}';
    END IF;
    IF char_length(v_after.description) > 1000 THEN
        v_errors := v_errors || '{"description": ["maxLength"]}';
    END IF;
    IF v_errors <> '{}' THEN
        PERFORM platform.fail_validation(v_errors);
    END IF;

    UPDATE dictionaries.positions p
    SET name = v_after.name, description = v_after.description, is_active = v_after.is_active
    WHERE p.id = p_id;

    PERFORM platform.audit('position', p_id::text, 'update',
        jsonb_build_object('name', v_before.name, 'description', v_before.description, 'isActive', v_before.is_active),
        jsonb_build_object('name', v_after.name, 'description', v_after.description, 'isActive', v_after.is_active));
END;
$$;
