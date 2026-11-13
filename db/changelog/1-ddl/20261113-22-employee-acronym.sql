--liquibase formatted sql

--changeset hr:20261113-22 splitStatements:false
--comment: Akronim kartoteki (decyzja właściciela 2026-11-13): odróżnia osoby o tym samym imieniu i nazwisku. Nadawany
--comment: przy założeniu jako RRRRMM daty dodania + 4 znaki z alfabetu bez mylących znaków; edytowalny, unikalny
--comment: w spółce bez względu na wielkość liter.
CREATE FUNCTION employees.new_acronym(p_company_id uuid, p_on date)
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
            FROM generate_series(1, 4)
        );
        EXIT WHEN NOT EXISTS (
            SELECT FROM employees.employees e
            WHERE e.company_id = p_company_id AND upper(e.acronym) = v_candidate
        );
    END LOOP;
    RETURN v_candidate;
END;
$$;

ALTER TABLE employees.employees ADD COLUMN acronym text NULL;
UPDATE employees.employees e
SET acronym = employees.new_acronym(e.company_id, e.created_at::date);
ALTER TABLE employees.employees ALTER COLUMN acronym SET NOT NULL;
ALTER TABLE employees.employees ADD CONSTRAINT employees_acronym CHECK (acronym ~ '^[A-Za-z0-9._-]{1,30}$');
CREATE UNIQUE INDEX employees_company_acronym_uq ON employees.employees (company_id, upper(acronym));

-- Każde założenie kartoteki (rutyna, dane deweloperskie, testy) dostaje akronim, gdy go nie podano.
CREATE FUNCTION employees.set_acronym()
    RETURNS trigger
    LANGUAGE plpgsql
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    IF NEW.acronym IS NULL THEN
        NEW.acronym := employees.new_acronym(NEW.company_id, current_date);
    END IF;
    RETURN NEW;
END;
$$;
CREATE TRIGGER employees_set_acronym BEFORE INSERT ON employees.employees
    FOR EACH ROW EXECUTE FUNCTION employees.set_acronym();
