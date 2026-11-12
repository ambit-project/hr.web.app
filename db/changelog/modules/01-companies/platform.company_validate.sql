--liquibase formatted sql

--changeset hr:platform.company_validate runOnChange:true splitStatements:false
--comment: Błędy pól spółki jako JSON {"pole": ["kod"]}; p_id = spółka edytowana (wyłączona ze sprawdzania duplikatów).
-- Zmiana listy parametrów: bez DROP powstałoby przeciążenie obok starej wersji.
DROP FUNCTION IF EXISTS platform.company_validate(uuid, text, text, text, text, text, text, text);
CREATE OR REPLACE FUNCTION platform.company_validate(
    p_id uuid,
    p_short_name text,
    p_name text,
    p_nip text,
    p_regon text,
    p_street text,
    p_postal_code text,
    p_city text,
    p_tax_office_id uuid
)
    RETURNS jsonb
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_errors jsonb := '{}';
BEGIN
    IF p_short_name IS NULL THEN
        v_errors := v_errors || '{"shortName": ["required"]}';
    ELSIF char_length(p_short_name) > 50 THEN
        v_errors := v_errors || '{"shortName": ["maxLength"]}';
    ELSIF EXISTS (
        SELECT FROM platform.companies c
        WHERE lower(c.short_name) = lower(p_short_name) AND c.id IS DISTINCT FROM p_id
    ) THEN
        v_errors := v_errors || '{"shortName": ["duplicate"]}';
    END IF;

    IF p_name IS NULL THEN
        v_errors := v_errors || '{"name": ["required"]}';
    ELSIF char_length(p_name) > 300 THEN
        v_errors := v_errors || '{"name": ["maxLength"]}';
    END IF;

    IF p_nip IS NULL THEN
        v_errors := v_errors || '{"nip": ["required"]}';
    ELSIF NOT platform.nip_valid(p_nip) THEN
        v_errors := v_errors || '{"nip": ["invalid"]}';
    ELSIF EXISTS (SELECT FROM platform.companies c WHERE c.nip = p_nip AND c.id IS DISTINCT FROM p_id) THEN
        v_errors := v_errors || '{"nip": ["duplicate"]}';
    END IF;

    IF p_regon IS NULL THEN
        v_errors := v_errors || '{"regon": ["required"]}';
    ELSIF NOT platform.regon_valid(p_regon) THEN
        v_errors := v_errors || '{"regon": ["invalid"]}';
    END IF;

    IF p_street IS NULL THEN
        v_errors := v_errors || '{"street": ["required"]}';
    END IF;

    IF p_postal_code IS NULL THEN
        v_errors := v_errors || '{"postalCode": ["required"]}';
    ELSIF p_postal_code !~ '^\d{2}-\d{3}$' THEN
        v_errors := v_errors || '{"postalCode": ["invalid"]}';
    END IF;

    IF p_city IS NULL THEN
        v_errors := v_errors || '{"city": ["required"]}';
    END IF;

    -- Nieaktywny urząd wolno zachować (spółka już go ma), ale nie wybrać na nowo.
    IF p_tax_office_id IS NULL THEN
        v_errors := v_errors || '{"taxOfficeId": ["required"]}';
    ELSIF NOT EXISTS (SELECT FROM dictionaries.tax_offices t WHERE t.id = p_tax_office_id) THEN
        v_errors := v_errors || '{"taxOfficeId": ["notFound"]}';
    ELSIF NOT EXISTS (SELECT FROM dictionaries.tax_offices t WHERE t.id = p_tax_office_id AND t.is_active)
      AND NOT EXISTS (
          SELECT FROM platform.companies c WHERE c.id = p_id AND c.tax_office_id = p_tax_office_id
      ) THEN
        v_errors := v_errors || '{"taxOfficeId": ["inactive"]}';
    END IF;

    RETURN v_errors;
END;
$$;
