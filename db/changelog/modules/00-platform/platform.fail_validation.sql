--liquibase formatted sql

--changeset hr:platform.fail_validation runOnChange:true splitStatements:false
--comment: Błąd walidacji U0001; HINT niesie JSON {"pole": ["kod", ...]} (katalog kodów: db/README.md).
CREATE OR REPLACE FUNCTION platform.fail_validation(p_errors jsonb)
    RETURNS void
    LANGUAGE plpgsql
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    RAISE EXCEPTION 'validation failed'
        USING ERRCODE = 'U0001', HINT = p_errors::text;
END;
$$;
