--liquibase formatted sql

--changeset hr:pay_components.type_validate runOnChange:true splitStatements:false
--comment: Błędy pól składnika (R1–R4). Cechy naliczenia tylko dla dodatku, kategoria tylko dla potrącenia.
CREATE OR REPLACE FUNCTION pay_components.type_validate(
    p_name text,
    p_kind text,
    p_calculation text,
    p_in_zus_base boolean,
    p_in_health_base boolean,
    p_taxable boolean,
    p_in_sickness_base boolean,
    p_deduction_category text
)
    RETURNS jsonb
    LANGUAGE plpgsql
    IMMUTABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_errors jsonb := '{}';
BEGIN
    IF p_name IS NULL THEN
        v_errors := v_errors || '{"name": ["required"]}';
    ELSIF char_length(p_name) > 200 THEN
        v_errors := v_errors || '{"name": ["maxLength"]}';
    END IF;
    IF p_kind IS NULL OR p_kind NOT IN ('addition', 'deduction') THEN
        RETURN v_errors || '{"kind": ["invalid"]}';
    END IF;

    IF p_calculation IS NULL THEN
        v_errors := v_errors || '{"calculation": ["required"]}';
    ELSIF p_calculation NOT IN ('fixed', 'percentOfBase', 'rateTimesHours')
        OR (p_kind = 'deduction' AND p_calculation <> 'fixed') THEN
        v_errors := v_errors || '{"calculation": ["invalid"]}';
    END IF;

    IF p_kind = 'addition' THEN
        IF p_in_zus_base IS NULL OR p_in_health_base IS NULL OR p_taxable IS NULL OR p_in_sickness_base IS NULL THEN
            v_errors := v_errors || '{"inZusBase": ["required"]}';
        END IF;
        IF p_deduction_category IS NOT NULL THEN
            v_errors := v_errors || '{"deductionCategory": ["invalid"]}';
        END IF;
    ELSE
        IF coalesce(p_in_zus_base, p_in_health_base, p_taxable, p_in_sickness_base) IS NOT NULL THEN
            v_errors := v_errors || '{"inZusBase": ["invalid"]}';
        END IF;
        IF p_deduction_category IS NULL THEN
            v_errors := v_errors || '{"deductionCategory": ["required"]}';
        ELSIF p_deduction_category NOT IN ('advance', 'penalty', 'voluntaryEmployer', 'voluntaryOther') THEN
            v_errors := v_errors || '{"deductionCategory": ["invalid"]}';
        END IF;
    END IF;
    RETURN v_errors;
END;
$$;

--changeset hr:pay_components.requires_consent runOnChange:true splitStatements:false
--comment: Potrącenia dobrowolne wymagają pisemnej zgody (KP art. 91 §1; R4).
CREATE OR REPLACE FUNCTION pay_components.requires_consent(p_deduction_category text)
    RETURNS boolean
    LANGUAGE sql
    IMMUTABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT coalesce(p_deduction_category IN ('voluntaryEmployer', 'voluntaryOther'), false);
$$;

--changeset hr:pay_components.consent_status runOnChange:true splitStatements:false
--comment: Status zgody w dniu: unsigned, future, active, ended (R13).
CREATE OR REPLACE FUNCTION pay_components.consent_status(p_signed_at date, p_from date, p_to date, p_on date)
    RETURNS text
    LANGUAGE sql
    IMMUTABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT CASE
        WHEN p_signed_at IS NULL THEN 'unsigned'
        WHEN p_on < p_from THEN 'future'
        WHEN p_on > p_to THEN 'ended'
        ELSE 'active'
    END;
$$;

--changeset hr:pay_components.consent_error runOnChange:true splitStatements:false
--comment: Czy zgoda pozwala na potrącenie w okresie i kwocie (R10): NULL — tak; consentMissing — nie ta kartoteka
--comment: albo składnik, niepodpisana albo nie obejmuje całego okresu; aboveConsent — kwota większa niż zgoda.
CREATE OR REPLACE FUNCTION pay_components.consent_error(
    p_consent_id uuid,
    p_employee_id uuid,
    p_component_type_id uuid,
    p_from date,
    p_to date,
    p_value numeric
)
    RETURNS text
    LANGUAGE sql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT CASE
        WHEN d.id IS NULL OR d.employee_id <> p_employee_id OR d.component_type_id <> p_component_type_id
            OR d.signed_at IS NULL
            OR NOT daterange(d.valid_from, d.valid_to, '[]') @> daterange(p_from, p_to, '[]')
            THEN 'consentMissing'
        WHEN p_value > d.monthly_amount THEN 'aboveConsent'
    END
    FROM (SELECT 1) one
    LEFT JOIN pay_components.consents d ON d.id = p_consent_id;
$$;

--changeset hr:pay_components.component_validate runOnChange:true splitStatements:false
--comment: Błędy przypisania składnika do umowy (R7–R10). p_id = NULL przy nowym przypisaniu.
CREATE OR REPLACE FUNCTION pay_components.component_validate(
    p_employment_id uuid,
    p_component_type_id uuid,
    p_value numeric,
    p_from date,
    p_to date,
    p_consent_id uuid
)
    RETURNS jsonb
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_errors jsonb := '{}';
    v_employment employment.employments%ROWTYPE;
    v_type pay_components.component_types%ROWTYPE;
    v_consent_error text;
BEGIN
    SELECT * INTO v_employment FROM employment.employments m WHERE m.id = p_employment_id;
    SELECT * INTO v_type FROM pay_components.component_types t WHERE t.id = p_component_type_id;

    IF p_component_type_id IS NULL THEN
        v_errors := v_errors || '{"componentTypeId": ["required"]}';
    ELSIF v_type.id IS NULL OR v_type.is_system THEN
        v_errors := v_errors || '{"componentTypeId": ["notFound"]}';
    ELSIF NOT v_type.is_active THEN
        v_errors := v_errors || '{"componentTypeId": ["inactive"]}';
    ELSIF v_type.deduction_category = 'penalty' AND v_employment.kind <> 'employment' THEN
        -- Kary porządkowe dotyczą pracowników (KP art. 108).
        v_errors := v_errors || '{"componentTypeId": ["invalid"]}';
    END IF;

    IF p_value IS NULL THEN
        v_errors := v_errors || '{"value": ["required"]}';
    ELSIF p_value <= 0 OR p_value <> round(p_value, 2) OR p_value >= 10000000000
        OR (v_type.calculation = 'percentOfBase' AND p_value > 1000) THEN
        v_errors := v_errors || '{"value": ["invalid"]}';
    END IF;

    IF p_from IS NULL THEN
        RETURN v_errors || '{"validFrom": ["required"]}';
    END IF;
    IF p_to < p_from THEN
        v_errors := v_errors || '{"validTo": ["beforeFrom"]}';
    -- Pusty koniec składnika = do końca umowy, więc sprawdzany jest okres do końca umowy.
    ELSIF NOT employment.period(v_employment.start_date, v_employment.end_date, v_employment.terminated_at)
        @> daterange(p_from, coalesce(p_to, v_employment.terminated_at, v_employment.end_date), '[]') THEN
        v_errors := v_errors || '{"validFrom": ["outsidePeriod"]}';
    END IF;

    IF v_type.id IS NOT NULL AND pay_components.requires_consent(v_type.deduction_category) THEN
        IF p_consent_id IS NULL THEN
            v_errors := v_errors || '{"consentId": ["consentMissing"]}';
        ELSIF v_errors = '{}' THEN
            v_consent_error := pay_components.consent_error(p_consent_id, v_employment.employee_id,
                p_component_type_id, p_from, p_to, p_value);
            IF v_consent_error = 'aboveConsent' THEN
                v_errors := v_errors || '{"value": ["aboveConsent"]}';
            ELSIF v_consent_error IS NOT NULL THEN
                v_errors := v_errors || jsonb_build_object('consentId', jsonb_build_array(v_consent_error));
            END IF;
        END IF;
    ELSIF p_consent_id IS NOT NULL THEN
        v_errors := v_errors || '{"consentId": ["invalid"]}';
    END IF;
    RETURN v_errors;
END;
$$;

--changeset hr:pay_components.garnishment_validate runOnChange:true splitStatements:false
--comment: Błędy pól zajęcia (R17, R18). p_id = NULL przy nowym zajęciu.
CREATE OR REPLACE FUNCTION pay_components.garnishment_validate(
    p_id uuid,
    p_employee_id uuid,
    p_kind text,
    p_authority_kind text,
    p_authority_name text,
    p_case_number text,
    p_received_at date,
    p_monthly_amount numeric,
    p_total_amount numeric,
    p_note text
)
    RETURNS jsonb
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_errors jsonb := '{}';
BEGIN
    IF p_kind IS NULL OR p_kind NOT IN ('alimony', 'other') THEN
        v_errors := v_errors || '{"kind": ["invalid"]}';
    END IF;
    IF p_authority_kind IS NULL THEN
        v_errors := v_errors || '{"authorityKind": ["required"]}';
    ELSIF p_authority_kind NOT IN ('court', 'administrative', 'creditor')
        OR (p_authority_kind = 'creditor' AND p_kind IS DISTINCT FROM 'alimony') THEN
        -- Bez egzekucji pracodawca potrąca tylko alimenty (KP art. 88).
        v_errors := v_errors || '{"authorityKind": ["invalid"]}';
    END IF;
    IF p_authority_name IS NULL THEN
        v_errors := v_errors || '{"authorityName": ["required"]}';
    ELSIF char_length(p_authority_name) > 300 THEN
        v_errors := v_errors || '{"authorityName": ["maxLength"]}';
    END IF;
    IF p_case_number IS NULL THEN
        v_errors := v_errors || '{"caseNumber": ["required"]}';
    ELSIF char_length(p_case_number) > 100 THEN
        v_errors := v_errors || '{"caseNumber": ["maxLength"]}';
    ELSIF p_authority_name IS NOT NULL AND EXISTS (
        SELECT FROM pay_components.garnishments g
        WHERE g.employee_id = p_employee_id AND g.id IS DISTINCT FROM p_id
          AND lower(g.authority_name) = lower(p_authority_name) AND lower(g.case_number) = lower(p_case_number)
    ) THEN
        v_errors := v_errors || '{"caseNumber": ["duplicate"]}';
    END IF;
    IF p_received_at IS NULL THEN
        v_errors := v_errors || '{"receivedAt": ["required"]}';
    ELSIF p_received_at > current_date THEN
        v_errors := v_errors || '{"receivedAt": ["future"]}';
    END IF;
    IF p_monthly_amount IS NOT NULL AND (p_monthly_amount <= 0 OR p_monthly_amount <> round(p_monthly_amount, 2)) THEN
        v_errors := v_errors || '{"monthlyAmount": ["invalid"]}';
    END IF;
    IF p_total_amount IS NOT NULL AND (p_total_amount <= 0 OR p_total_amount <> round(p_total_amount, 2)) THEN
        v_errors := v_errors || '{"totalAmount": ["invalid"]}';
    ELSIF p_total_amount IS NULL AND (p_kind = 'other' OR (p_kind = 'alimony' AND p_monthly_amount IS NULL)) THEN
        v_errors := v_errors || '{"totalAmount": ["required"]}';
    END IF;
    IF char_length(p_note) > 2000 THEN
        v_errors := v_errors || '{"note": ["maxLength"]}';
    END IF;
    RETURN v_errors;
END;
$$;
