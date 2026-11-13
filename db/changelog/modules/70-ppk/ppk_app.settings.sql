--liquibase formatted sql

--changeset hr:ppk_app.company_settings_list runOnChange:true splitStatements:false
--comment: Okresy ustawień PPK spółki, od najnowszego (R1; Kadry, Administrator). Stawka w procentach.
DROP FUNCTION IF EXISTS ppk_app.company_settings_list(uuid);
CREATE FUNCTION ppk_app.company_settings_list(p_company_id uuid)
    RETURNS TABLE (
        id uuid,
        valid_from date,
        valid_to date,
        institution_name text,
        agreement_number text,
        agreement_date date,
        employer_additional_rate numeric
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
    SELECT s.id, s.valid_from, s.valid_to, s.institution_name, s.agreement_number, s.agreement_date,
           s.employer_additional_rate
    FROM ppk.company_settings s
    WHERE s.company_id = p_company_id
    ORDER BY s.valid_from DESC;
END;
$$;

--changeset hr:ppk_app.company_settings_set runOnChange:true splitStatements:false
--comment: Nowy okres ustawień PPK spółki (R1): od 1. dnia miesiąca (art. 26 ust. 4), nie wcześniej niż ostatni okres;
--comment: ten sam dzień — zmiana ostatniego okresu, późniejszy — poprzedni okres kończy się dzień wcześniej.
--comment: Wpłata dodatkowa pracodawcy 0–2,5% (art. 26 ust. 2).
CREATE OR REPLACE PROCEDURE ppk_app.company_settings_set(
    IN p_company_id uuid,
    IN p_valid_from date,
    IN p_institution_name text,
    IN p_agreement_number text,
    IN p_agreement_date date,
    IN p_employer_additional_rate numeric,
    INOUT p_id uuid
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_institution text := nullif(btrim(p_institution_name), '');
    v_number text := nullif(btrim(p_agreement_number), '');
    v_last ppk.company_settings%ROWTYPE;
    v_after ppk.company_settings%ROWTYPE;
    v_errors jsonb := '{}';
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    PERFORM FROM platform.companies c WHERE c.id = p_company_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'company not found' USING ERRCODE = 'U0002';
    END IF;
    SELECT * INTO v_last FROM ppk.company_settings s
    WHERE s.company_id = p_company_id ORDER BY s.valid_from DESC LIMIT 1;

    IF p_valid_from IS NULL THEN
        v_errors := v_errors || '{"validFrom": ["required"]}';
    ELSIF extract(day FROM p_valid_from) <> 1 THEN
        v_errors := v_errors || '{"validFrom": ["invalid"]}';
    ELSIF p_valid_from < v_last.valid_from THEN
        v_errors := v_errors || '{"validFrom": ["beforeCurrent"]}';
    END IF;
    IF v_institution IS NULL THEN
        v_errors := v_errors || '{"institutionName": ["required"]}';
    ELSIF char_length(v_institution) > 200 THEN
        v_errors := v_errors || '{"institutionName": ["maxLength"]}';
    END IF;
    IF v_number IS NULL THEN
        v_errors := v_errors || '{"agreementNumber": ["required"]}';
    ELSIF char_length(v_number) > 60 THEN
        v_errors := v_errors || '{"agreementNumber": ["maxLength"]}';
    END IF;
    IF p_agreement_date IS NULL THEN
        v_errors := v_errors || '{"agreementDate": ["required"]}';
    ELSIF p_agreement_date > current_date THEN
        v_errors := v_errors || '{"agreementDate": ["future"]}';
    END IF;
    IF p_employer_additional_rate IS NULL THEN
        v_errors := v_errors || '{"employerAdditionalRate": ["required"]}';
    ELSIF p_employer_additional_rate NOT BETWEEN 0 AND 2.5
        OR p_employer_additional_rate <> round(p_employer_additional_rate, 2) THEN
        v_errors := v_errors || '{"employerAdditionalRate": ["invalid"]}';
    END IF;
    IF v_errors <> '{}' THEN
        PERFORM platform.fail_validation(v_errors);
    END IF;

    IF v_last.id IS NOT NULL AND v_last.valid_from = p_valid_from THEN
        UPDATE ppk.company_settings s
        SET institution_name = v_institution, agreement_number = v_number, agreement_date = p_agreement_date,
            employer_additional_rate = p_employer_additional_rate
        WHERE s.id = v_last.id
        RETURNING * INTO v_after;
        p_id := v_after.id;
        PERFORM platform.audit('ppk_company_settings', p_id::text, 'update', to_jsonb(v_last), to_jsonb(v_after));
        RETURN;
    END IF;

    IF v_last.id IS NOT NULL AND (v_last.valid_to IS NULL OR v_last.valid_to >= p_valid_from) THEN
        UPDATE ppk.company_settings s SET valid_to = p_valid_from - 1 WHERE s.id = v_last.id RETURNING * INTO v_after;
        PERFORM platform.audit('ppk_company_settings', v_last.id::text, 'update', to_jsonb(v_last),
            to_jsonb(v_after));
    END IF;
    INSERT INTO ppk.company_settings (company_id, valid_from, institution_name, agreement_number, agreement_date,
                                      employer_additional_rate, created_by)
    VALUES (p_company_id, p_valid_from, v_institution, v_number, p_agreement_date, p_employer_additional_rate,
            platform.actor_id())
    RETURNING * INTO v_after;
    p_id := v_after.id;
    PERFORM platform.audit('ppk_company_settings', p_id::text, 'create', NULL, to_jsonb(v_after));
END;
$$;
