--liquibase formatted sql

--changeset hr:pay_components_app.component_type_list runOnChange:true splitStatements:false
--comment: Słownik składników (R1–R6; Kadry i Administrator). Systemowe na końcu, potem według kodu.
CREATE OR REPLACE FUNCTION pay_components_app.component_type_list(p_include_inactive boolean)
    RETURNS TABLE (
        id uuid,
        code text,
        name text,
        kind text,
        calculation text,
        in_zus_base boolean,
        in_health_base boolean,
        taxable boolean,
        in_sickness_base boolean,
        deduction_category text,
        requires_consent boolean,
        is_system boolean,
        is_active boolean,
        is_used boolean,
        row_version bigint
    )
    LANGUAGE plpgsql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    RETURN QUERY
    SELECT t.id, t.code, t.name, t.kind, t.calculation, t.in_zus_base, t.in_health_base, t.taxable,
           t.in_sickness_base, t.deduction_category, pay_components.requires_consent(t.deduction_category),
           t.is_system, t.is_active,
           EXISTS (SELECT FROM pay_components.employment_components c WHERE c.component_type_id = t.id)
               OR EXISTS (SELECT FROM pay_components.consents d WHERE d.component_type_id = t.id),
           t.xmin::text::bigint
    FROM pay_components.component_types t
    WHERE coalesce(p_include_inactive, false) OR t.is_active
    ORDER BY t.is_system, t.code;
END;
$$;

--changeset hr:pay_components_app.component_type_create runOnChange:true splitStatements:false
--comment: Nowy składnik (R1–R4, R6; Administrator). Kod zapisywany wielkimi literami.
CREATE OR REPLACE PROCEDURE pay_components_app.component_type_create(
    IN p_code text,
    IN p_name text,
    IN p_kind text,
    IN p_calculation text,
    IN p_in_zus_base boolean,
    IN p_in_health_base boolean,
    IN p_taxable boolean,
    IN p_in_sickness_base boolean,
    IN p_deduction_category text,
    INOUT p_id uuid
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_code text := upper(nullif(btrim(p_code), ''));
    v_errors jsonb;
    v_row pay_components.component_types%ROWTYPE;
BEGIN
    PERFORM platform.require_role('administrator');
    v_errors := pay_components.type_validate(nullif(btrim(p_name), ''), p_kind, p_calculation, p_in_zus_base,
        p_in_health_base, p_taxable, p_in_sickness_base, nullif(btrim(p_deduction_category), ''));
    IF v_code IS NULL THEN
        v_errors := v_errors || '{"code": ["required"]}';
    ELSIF v_code !~ '^[A-Z0-9-]{1,20}$' THEN
        v_errors := v_errors || '{"code": ["invalid"]}';
    ELSIF EXISTS (SELECT FROM pay_components.component_types t WHERE t.code = v_code) THEN
        v_errors := v_errors || '{"code": ["duplicate"]}';
    END IF;
    IF v_errors <> '{}' THEN
        PERFORM platform.fail_validation(v_errors);
    END IF;

    INSERT INTO pay_components.component_types (code, name, kind, calculation, in_zus_base, in_health_base, taxable,
                                                in_sickness_base, deduction_category, created_by)
    VALUES (v_code, btrim(p_name), p_kind, p_calculation, p_in_zus_base, p_in_health_base, p_taxable,
            p_in_sickness_base, nullif(btrim(p_deduction_category), ''), platform.actor_id())
    RETURNING * INTO v_row;
    p_id := v_row.id;
    PERFORM platform.audit('pay_component_type', p_id::text, 'create', NULL, to_jsonb(v_row));
END;
$$;

--changeset hr:pay_components_app.component_type_update runOnChange:true splitStatements:false
--comment: Zmiana składnika (R6; Administrator). Kod i rodzaj niezmienne; systemowe tylko do odczytu; sposobu
--comment: liczenia nie zmienia się, gdy składnik jest przypisany (componentInUse).
CREATE OR REPLACE PROCEDURE pay_components_app.component_type_update(
    IN p_id uuid,
    IN p_row_version bigint,
    IN p_name text,
    IN p_calculation text,
    IN p_in_zus_base boolean,
    IN p_in_health_base boolean,
    IN p_taxable boolean,
    IN p_in_sickness_base boolean,
    IN p_deduction_category text,
    IN p_is_active boolean
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_before pay_components.component_types%ROWTYPE;
    v_after pay_components.component_types%ROWTYPE;
    v_errors jsonb;
BEGIN
    PERFORM platform.require_role('administrator');
    SELECT * INTO v_before FROM pay_components.component_types t WHERE t.id = p_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'pay component type not found' USING ERRCODE = 'U0002';
    END IF;
    IF v_before.is_system THEN
        RAISE EXCEPTION 'system pay component is read-only' USING ERRCODE = 'U0003', HINT = 'systemComponent';
    END IF;
    IF (SELECT t.xmin::text::bigint FROM pay_components.component_types t WHERE t.id = p_id) <> p_row_version THEN
        RAISE EXCEPTION 'pay component type changed' USING ERRCODE = 'U0003';
    END IF;

    v_errors := pay_components.type_validate(nullif(btrim(p_name), ''), v_before.kind, p_calculation, p_in_zus_base,
        p_in_health_base, p_taxable, p_in_sickness_base, nullif(btrim(p_deduction_category), ''));
    IF p_is_active IS NULL THEN
        v_errors := v_errors || '{"isActive": ["required"]}';
    END IF;
    IF v_errors <> '{}' THEN
        PERFORM platform.fail_validation(v_errors);
    END IF;
    IF p_calculation <> v_before.calculation
        AND EXISTS (SELECT FROM pay_components.employment_components c WHERE c.component_type_id = p_id) THEN
        RAISE EXCEPTION 'pay component is assigned' USING ERRCODE = 'U0003', HINT = 'componentInUse';
    END IF;

    UPDATE pay_components.component_types t
    SET name = btrim(p_name), calculation = p_calculation, in_zus_base = p_in_zus_base,
        in_health_base = p_in_health_base, taxable = p_taxable, in_sickness_base = p_in_sickness_base,
        deduction_category = nullif(btrim(p_deduction_category), ''), is_active = p_is_active
    WHERE t.id = p_id
    RETURNING * INTO v_after;
    PERFORM platform.audit('pay_component_type', p_id::text, 'update', to_jsonb(v_before), to_jsonb(v_after));
END;
$$;
