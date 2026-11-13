--liquibase formatted sql

--changeset hr:pay_components_app.employment_component_list runOnChange:true splitStatements:false
--comment: Składniki umowy ze statusem (R12; Kadry, Administrator). Kwoty — zapisuje audyt odczytu, więc VOLATILE.
CREATE OR REPLACE FUNCTION pay_components_app.employment_component_list(p_employment_id uuid)
    RETURNS TABLE (
        id uuid,
        component_type_id uuid,
        code text,
        name text,
        kind text,
        calculation text,
        value numeric,
        valid_from date,
        valid_to date,
        consent_id uuid,
        status text,
        row_version bigint
    )
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_employee_id uuid;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    SELECT m.employee_id INTO v_employee_id FROM employment.employments m WHERE m.id = p_employment_id;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'employment not found' USING ERRCODE = 'U0002';
    END IF;
    PERFORM platform.read_audit(v_employee_id, 'employment_components', p_employment_id::text);

    RETURN QUERY
    SELECT c.id, t.id, t.code, t.name, t.kind, t.calculation, c.value, c.valid_from, c.valid_to, c.consent_id,
           employment.status(c.valid_from, c.valid_to, NULL, current_date), c.xmin::text::bigint
    FROM pay_components.employment_components c
    JOIN pay_components.component_types t ON t.id = c.component_type_id
    WHERE c.employment_id = p_employment_id
    ORDER BY c.valid_from DESC, t.code;
END;
$$;

--changeset hr:pay_components_app.employment_component_add runOnChange:true splitStatements:false
--comment: Przypisanie składnika do umowy (R7–R10). Okres w okresie zamkniętej listy płac — U0003 payrollClosed,
--comment: naliczonej — lista nieaktualna (009 R2, R25).
CREATE OR REPLACE PROCEDURE pay_components_app.employment_component_add(
    IN p_employment_id uuid,
    IN p_component_type_id uuid,
    IN p_value numeric,
    IN p_valid_from date,
    IN p_valid_to date,
    IN p_consent_id uuid,
    INOUT p_id uuid
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_errors jsonb;
    v_row pay_components.employment_components%ROWTYPE;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    PERFORM FROM employment.employments m WHERE m.id = p_employment_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'employment not found' USING ERRCODE = 'U0002';
    END IF;
    -- Zgoda nie może się zmienić w trakcie sprawdzania.
    PERFORM FROM pay_components.consents d WHERE d.id = p_consent_id FOR SHARE;

    v_errors := pay_components.component_validate(p_employment_id, p_component_type_id, p_value, p_valid_from,
        p_valid_to, p_consent_id);
    IF v_errors <> '{}' THEN
        PERFORM platform.fail_validation(v_errors);
    END IF;

    PERFORM payroll.guard(NULL, p_employment_id, p_valid_from, p_valid_to);

    INSERT INTO pay_components.employment_components (employment_id, component_type_id, value, valid_from, valid_to,
                                                      consent_id, created_by)
    VALUES (p_employment_id, p_component_type_id, p_value, p_valid_from, p_valid_to, p_consent_id,
            platform.actor_id())
    RETURNING * INTO v_row;
    p_id := v_row.id;
    PERFORM platform.audit('employment_component', p_id::text, 'create', NULL, to_jsonb(v_row));
EXCEPTION
    WHEN exclusion_violation THEN
        PERFORM platform.fail_validation('{"validFrom": ["overlap"]}');
END;
$$;

--changeset hr:pay_components_app.employment_component_end runOnChange:true splitStatements:false
--comment: Zakończenie przypisania datą albo cofnięcie zakończenia pustą datą (R11); sprawdza okres i zgodę jak przy dodaniu.
--comment: Zmienione dni (między starym a nowym końcem) w okresie zamkniętej listy — U0003 payrollClosed (009 R25).
CREATE OR REPLACE PROCEDURE pay_components_app.employment_component_end(
    IN p_id uuid,
    IN p_row_version bigint,
    IN p_valid_to date
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_before pay_components.employment_components%ROWTYPE;
    v_errors jsonb;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    SELECT * INTO v_before FROM pay_components.employment_components c WHERE c.id = p_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'employment component not found' USING ERRCODE = 'U0002';
    END IF;
    IF (SELECT c.xmin::text::bigint FROM pay_components.employment_components c WHERE c.id = p_id) <> p_row_version
    THEN
        RAISE EXCEPTION 'employment component changed' USING ERRCODE = 'U0003';
    END IF;

    v_errors := pay_components.component_validate(v_before.employment_id, v_before.component_type_id, v_before.value,
        v_before.valid_from, p_valid_to, v_before.consent_id);
    -- Składnik nieaktywny w słowniku można nadal zakończyć.
    v_errors := v_errors - 'componentTypeId';
    IF v_errors ? 'validTo' OR v_errors ? 'validFrom' THEN
        PERFORM platform.fail_validation(jsonb_build_object('validTo',
            coalesce(v_errors -> 'validTo', v_errors -> 'validFrom')));
    ELSIF v_errors <> '{}' THEN
        PERFORM platform.fail_validation(v_errors);
    END IF;

    IF v_before.valid_to IS DISTINCT FROM p_valid_to THEN
        PERFORM payroll.guard(NULL, v_before.employment_id,
            least(coalesce(v_before.valid_to, 'infinity'), coalesce(p_valid_to, 'infinity')) + 1,
            nullif(greatest(coalesce(v_before.valid_to, 'infinity'), coalesce(p_valid_to, 'infinity')), 'infinity'));
    END IF;
    UPDATE pay_components.employment_components c SET valid_to = p_valid_to WHERE c.id = p_id;
    PERFORM platform.audit('employment_component', p_id::text, 'update',
        jsonb_build_object('valid_to', v_before.valid_to), jsonb_build_object('valid_to', p_valid_to));
EXCEPTION
    WHEN exclusion_violation THEN
        PERFORM platform.fail_validation('{"validTo": ["overlap"]}');
END;
$$;

--changeset hr:pay_components_app.employment_component_delete runOnChange:true splitStatements:false
--comment: Usunięcie przypisania wprowadzonego przez pomyłkę (R11). Okres w okresie zamkniętej listy płac (składnik
--comment: użyty na liście) — U0003 payrollClosed, naliczonej — lista nieaktualna (009 R25).
CREATE OR REPLACE PROCEDURE pay_components_app.employment_component_delete(IN p_id uuid, IN p_row_version bigint)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_row pay_components.employment_components%ROWTYPE;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    SELECT * INTO v_row FROM pay_components.employment_components c WHERE c.id = p_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'employment component not found' USING ERRCODE = 'U0002';
    END IF;
    IF (SELECT c.xmin::text::bigint FROM pay_components.employment_components c WHERE c.id = p_id) <> p_row_version
    THEN
        RAISE EXCEPTION 'employment component changed' USING ERRCODE = 'U0003';
    END IF;
    PERFORM payroll.guard(NULL, v_row.employment_id, v_row.valid_from, v_row.valid_to);
    DELETE FROM pay_components.employment_components c WHERE c.id = p_id;
    PERFORM platform.audit('employment_component', p_id::text, 'delete', to_jsonb(v_row), NULL);
END;
$$;
