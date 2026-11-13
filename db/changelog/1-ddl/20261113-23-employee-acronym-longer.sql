--liquibase formatted sql

--changeset hr:20261113-23 splitStatements:false
--comment: Dłuższy kod w akronimie (decyzja właściciela 2026-11-13): RRRRMM + 8 znaków zamiast 4 — mniej podobnych
--comment: akronimów. Akronimy nadane automatycznie (RRRRMM + 4 znaki) dostają nowy kod; zmienione ręcznie zostają.
CREATE OR REPLACE FUNCTION employees.new_acronym(p_company_id uuid, p_on date)
    RETURNS text
    LANGUAGE plpgsql
    VOLATILE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    -- Bez 0/O, 1/I/L — akronim bywa przepisywany z kartki.
    v_alphabet constant text := 'ABCDEFGHJKMNPQRSTUVWXYZ23456789';
    v_candidate text;
BEGIN
    LOOP
        v_candidate := to_char(p_on, 'YYYYMM') || (
            SELECT string_agg(substr(v_alphabet, 1 + floor(random() * length(v_alphabet))::integer, 1), '')
            FROM generate_series(1, 8)
        );
        EXIT WHEN NOT EXISTS (
            SELECT FROM employees.employees e
            WHERE e.company_id = p_company_id AND upper(e.acronym) = v_candidate
        );
    END LOOP;
    RETURN v_candidate;
END;
$$;

UPDATE employees.employees e
SET acronym = employees.new_acronym(e.company_id, to_date(left(e.acronym, 6), 'YYYYMM'))
WHERE e.acronym ~ '^\d{6}[A-HJKMNP-Z2-9]{4}$';
