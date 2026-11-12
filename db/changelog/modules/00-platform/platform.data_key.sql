--liquibase formatted sql

--changeset hr:platform.data_key runOnChange:true splitStatements:false
--comment: Klucz danych osobowych z ustawienia sesji hr.data_key (ustawia API, ADR-0009). Brak klucza = błąd, nigdy
--comment: szyfrowanie pustym kluczem.
CREATE OR REPLACE FUNCTION platform.data_key()
    RETURNS text
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_key text := current_setting('hr.data_key', true);
BEGIN
    IF v_key IS NULL OR char_length(v_key) < 32 THEN
        RAISE EXCEPTION 'data key is not set (hr.data_key, ADR-0009)';
    END IF;
    RETURN v_key;
END;
$$;
