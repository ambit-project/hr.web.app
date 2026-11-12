--liquibase formatted sql

--changeset hr:platform.legal_parameter runOnChange:true splitStatements:false
--comment: Wartość parametru w dniu (R21). Brak wartości to błąd U0006, nigdy zero.
CREATE OR REPLACE FUNCTION platform.legal_parameter(p_code text, p_on date)
    RETURNS numeric
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_value numeric;
BEGIN
    SELECT p.value INTO v_value
    FROM platform.legal_parameters p
    WHERE p.code = p_code AND daterange(p.valid_from, p.valid_to, '[]') @> p_on;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'missing legal parameter % on %', p_code, p_on
            USING ERRCODE = 'U0006', HINT = jsonb_build_object('code', p_code, 'on', p_on)::text;
    END IF;
    RETURN v_value;
END;
$$;

--changeset hr:platform.legal_parameter_or_null runOnChange:true splitStatements:false
--comment: Wartość parametru w dniu albo NULL — tylko dla sprawdzeń, które przy braku parametru się nie odbywają
--comment: (np. płaca minimalna dla umów sprzed okresu wprowadzonych parametrów, 004 R13). Naliczenia używają
--comment: legal_parameter (brak = U0006).
CREATE OR REPLACE FUNCTION platform.legal_parameter_or_null(p_code text, p_on date)
    RETURNS numeric
    LANGUAGE sql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT p.value
    FROM platform.legal_parameters p
    WHERE p.code = p_code AND daterange(p.valid_from, p.valid_to, '[]') @> p_on;
$$;
