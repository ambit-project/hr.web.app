--liquibase formatted sql

--changeset hr:platform.nip_valid runOnChange:true splitStatements:false
--comment: Suma kontrolna NIP (wagi 6,5,7,2,3,4,5,6,7; reszta 10 = NIP niepoprawny).
CREATE OR REPLACE FUNCTION platform.nip_valid(p_nip text)
    RETURNS boolean
    LANGUAGE sql
    IMMUTABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT p_nip ~ '^\d{10}$' AND (
        SELECT sum(substr(p_nip, i::int, 1)::int * w) % 11
        FROM unnest(ARRAY[6, 5, 7, 2, 3, 4, 5, 6, 7]) WITH ORDINALITY AS t(w, i)
    ) = substr(p_nip, 10, 1)::int;
$$;

--changeset hr:platform.regon_valid runOnChange:true splitStatements:false
--comment: Suma kontrolna REGON 9- i 14-cyfrowego (reszta 10 liczy się jako 0).
CREATE OR REPLACE FUNCTION platform.regon_valid(p_regon text)
    RETURNS boolean
    LANGUAGE sql
    IMMUTABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    WITH weights AS (
        SELECT CASE length(p_regon)
                   WHEN 9 THEN ARRAY[8, 9, 2, 3, 4, 5, 6, 7]
                   WHEN 14 THEN ARRAY[2, 4, 8, 5, 0, 9, 7, 3, 6, 1, 2, 4, 8]
               END AS w
    )
    SELECT p_regon ~ '^(\d{9}|\d{14})$' AND (
        SELECT (sum(substr(p_regon, i::int, 1)::int * x) % 11) % 10
        FROM weights, unnest(weights.w) WITH ORDINALITY AS t(x, i)
    ) = right(p_regon, 1)::int;
$$;

--changeset hr:platform.digits runOnChange:true splitStatements:false
--comment: Same cyfry z tekstu (NIP i REGON wpisywane z kreskami i spacjami).
CREATE OR REPLACE FUNCTION platform.digits(p_text text)
    RETURNS text
    LANGUAGE sql
    IMMUTABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT nullif(regexp_replace(coalesce(p_text, ''), '\D', '', 'g'), '');
$$;
