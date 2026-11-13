--liquibase formatted sql

--changeset hr:payroll_app.company_settings_list runOnChange:true splitStatements:false
--comment: Okresy ustawień płacowych spółki, od najnowszego (R6, R13; Kadry, Administrator). Stopa wypadkowa jako
--comment: ułamek (0,0167 = 1,67%).
DROP FUNCTION IF EXISTS payroll_app.company_settings_list(uuid);
CREATE FUNCTION payroll_app.company_settings_list(p_company_id uuid)
    RETURNS TABLE (
        id uuid,
        valid_from date,
        valid_to date,
        accident_rate numeric,
        pays_sickness_allowance boolean
    )
    LANGUAGE plpgsql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    IF NOT EXISTS (SELECT FROM platform.companies c WHERE c.id = p_company_id) THEN
        RAISE EXCEPTION 'company not found' USING ERRCODE = 'U0002';
    END IF;
    RETURN QUERY
    SELECT s.id, s.valid_from, s.valid_to, s.accident_rate, s.pays_sickness_allowance
    FROM payroll.company_settings s
    WHERE s.company_id = p_company_id
    ORDER BY s.valid_from DESC;
END;
$$;

--changeset hr:payroll_app.company_settings_set runOnChange:true splitStatements:false
--comment: Nowy okres ustawień płacowych spółki od 1. dnia miesiąca, nie wcześniej niż ostatni okres; ten sam dzień —
--comment: zmiana ostatniego okresu, późniejszy — poprzedni kończy się dzień wcześniej. Stopa wypadkowa 0 < x < 0,2;
--comment: zmiana dotyka okresów list (blokada zamkniętych, nieaktualność naliczonych — R2, R25).
CREATE OR REPLACE PROCEDURE payroll_app.company_settings_set(
    IN p_company_id uuid,
    IN p_valid_from date,
    IN p_accident_rate numeric,
    IN p_pays_sickness_allowance boolean,
    INOUT p_id uuid
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_last payroll.company_settings%ROWTYPE;
    v_after payroll.company_settings%ROWTYPE;
    v_errors jsonb := '{}';
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    PERFORM FROM platform.companies c WHERE c.id = p_company_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'company not found' USING ERRCODE = 'U0002';
    END IF;
    SELECT * INTO v_last FROM payroll.company_settings s
    WHERE s.company_id = p_company_id ORDER BY s.valid_from DESC LIMIT 1;

    IF p_valid_from IS NULL THEN
        v_errors := v_errors || '{"validFrom": ["required"]}';
    ELSIF extract(day FROM p_valid_from) <> 1 THEN
        v_errors := v_errors || '{"validFrom": ["invalid"]}';
    ELSIF p_valid_from < v_last.valid_from THEN
        v_errors := v_errors || '{"validFrom": ["beforeCurrent"]}';
    END IF;
    IF p_accident_rate IS NULL THEN
        v_errors := v_errors || '{"accidentRate": ["required"]}';
    ELSIF p_accident_rate <= 0 OR p_accident_rate >= 0.2 OR p_accident_rate <> round(p_accident_rate, 4) THEN
        v_errors := v_errors || '{"accidentRate": ["invalid"]}';
    END IF;
    IF p_pays_sickness_allowance IS NULL THEN
        v_errors := v_errors || '{"paysSicknessAllowance": ["required"]}';
    END IF;
    IF v_errors <> '{}' THEN
        PERFORM platform.fail_validation(v_errors);
    END IF;

    PERFORM payroll.guard(e.id, NULL, p_valid_from, NULL)
    FROM employees.employees e
    WHERE e.company_id = p_company_id;

    IF v_last.id IS NOT NULL AND v_last.valid_from = p_valid_from THEN
        UPDATE payroll.company_settings s
        SET accident_rate = p_accident_rate, pays_sickness_allowance = p_pays_sickness_allowance
        WHERE s.id = v_last.id
        RETURNING * INTO v_after;
        p_id := v_after.id;
        PERFORM platform.audit('payroll_company_settings', p_id::text, 'update', to_jsonb(v_last), to_jsonb(v_after));
        RETURN;
    END IF;

    IF v_last.id IS NOT NULL AND (v_last.valid_to IS NULL OR v_last.valid_to >= p_valid_from) THEN
        UPDATE payroll.company_settings s SET valid_to = p_valid_from - 1 WHERE s.id = v_last.id RETURNING * INTO v_after;
        PERFORM platform.audit('payroll_company_settings', v_last.id::text, 'update', to_jsonb(v_last),
            to_jsonb(v_after));
    END IF;
    INSERT INTO payroll.company_settings (company_id, valid_from, accident_rate, pays_sickness_allowance, created_by)
    VALUES (p_company_id, p_valid_from, p_accident_rate, p_pays_sickness_allowance, platform.actor_id())
    RETURNING * INTO v_after;
    p_id := v_after.id;
    PERFORM platform.audit('payroll_company_settings', p_id::text, 'create', NULL, to_jsonb(v_after));
END;
$$;
