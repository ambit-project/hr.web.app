--liquibase formatted sql

--changeset hr:employees.pesel_birth_date runOnChange:true splitStatements:false
--comment: Data urodzenia zapisana w PESEL (miesiąc +80 = 1800–1899, +0 = 1900–1999, +20 = 2000–2099, +40, +60); NULL,
--comment: gdy data nie istnieje.
CREATE OR REPLACE FUNCTION employees.pesel_birth_date(p_pesel text)
    RETURNS date
    LANGUAGE plpgsql
    IMMUTABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_year int;
    v_month int;
    v_century int;
BEGIN
    IF p_pesel !~ '^\d{11}$' THEN
        RETURN NULL;
    END IF;
    v_year := substr(p_pesel, 1, 2)::int;
    v_month := substr(p_pesel, 3, 2)::int;
    v_century := CASE v_month / 20 WHEN 4 THEN 1800 WHEN 0 THEN 1900 WHEN 1 THEN 2000 WHEN 2 THEN 2100 WHEN 3 THEN 2200 END;
    RETURN make_date(v_century + v_year, v_month % 20, substr(p_pesel, 5, 2)::int);
EXCEPTION
    WHEN datetime_field_overflow OR invalid_datetime_format OR numeric_value_out_of_range THEN
        RETURN NULL;
END;
$$;

--changeset hr:employees.pesel_valid runOnChange:true splitStatements:false
--comment: PESEL: 11 cyfr, suma kontrolna (wagi 1,3,7,9,1,3,7,9,1,3) i istniejąca data urodzenia.
CREATE OR REPLACE FUNCTION employees.pesel_valid(p_pesel text)
    RETURNS boolean
    LANGUAGE sql
    IMMUTABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT coalesce(p_pesel ~ '^\d{11}$', false)
       AND (10 - (
               SELECT sum(substr(p_pesel, i::int, 1)::int * w) % 10
               FROM unnest(ARRAY[1, 3, 7, 9, 1, 3, 7, 9, 1, 3]) WITH ORDINALITY AS t(w, i)
           )) % 10 = substr(p_pesel, 11, 1)::int
       AND employees.pesel_birth_date(p_pesel) IS NOT NULL;
$$;

--changeset hr:employees.iban_normalize runOnChange:true splitStatements:false
--comment: Rachunek bez spacji i kresek, wielkimi literami; same 26 cyfr = rachunek polski (dopisuje PL).
CREATE OR REPLACE FUNCTION employees.iban_normalize(p_iban text)
    RETURNS text
    LANGUAGE sql
    IMMUTABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT CASE WHEN v ~ '^\d{26}$' THEN 'PL' || v ELSE v END
    FROM (SELECT nullif(upper(regexp_replace(coalesce(p_iban, ''), '[\s-]', '', 'g')), '') AS v) n;
$$;

--changeset hr:employees.iban_valid runOnChange:true splitStatements:false
--comment: Polski IBAN: PL + 26 cyfr, cyfry kontrolne mod 97 = 1 (P = 25, L = 21).
CREATE OR REPLACE FUNCTION employees.iban_valid(p_iban text)
    RETURNS boolean
    LANGUAGE sql
    IMMUTABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT coalesce(p_iban ~ '^PL\d{26}$', false)
       AND (substr(p_iban, 5) || '2521' || substr(p_iban, 3, 2))::numeric % 97 = 1;
$$;

--changeset hr:employees.crypto runOnChange:true splitStatements:false
--comment: Szyfrowanie, odszyfrowanie i HMAC kluczem z platform.data_key() (ADR-0009).
CREATE OR REPLACE FUNCTION employees.encrypt(p_value text)
    RETURNS bytea
    LANGUAGE sql
    VOLATILE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT CASE WHEN p_value IS NULL THEN NULL ELSE ext.pgp_sym_encrypt(p_value, platform.data_key()) END;
$$;

CREATE OR REPLACE FUNCTION employees.decrypt(p_value bytea)
    RETURNS text
    LANGUAGE sql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT CASE WHEN p_value IS NULL THEN NULL ELSE ext.pgp_sym_decrypt(p_value, platform.data_key()) END;
$$;

CREATE OR REPLACE FUNCTION employees.pesel_hash(p_pesel text)
    RETURNS bytea
    LANGUAGE sql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT CASE WHEN p_pesel IS NULL THEN NULL ELSE ext.hmac(p_pesel, platform.data_key(), 'sha256') END;
$$;

--changeset hr:employees.employee_validate runOnChange:true splitStatements:false
--comment: Błędy pól kartoteki jako JSON {"pole": ["kod"]}; p_id = edytowana kartoteka (poza sprawdzaniem duplikatów).
CREATE OR REPLACE FUNCTION employees.employee_validate(
    p_id uuid,
    p_company_id uuid,
    p_number text,
    p_first_name text,
    p_last_name text,
    p_pesel text,
    p_identity_document text,
    p_birth_date date,
    p_tax_office_id uuid,
    p_email text
)
    RETURNS jsonb
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_errors jsonb := '{}';
BEGIN
    IF p_number IS NULL THEN
        v_errors := v_errors || '{"number": ["required"]}';
    ELSIF char_length(p_number) > 20 THEN
        v_errors := v_errors || '{"number": ["maxLength"]}';
    ELSIF EXISTS (
        SELECT FROM employees.employees e
        WHERE e.company_id = p_company_id AND e.number = p_number AND e.id IS DISTINCT FROM p_id
    ) THEN
        v_errors := v_errors || '{"number": ["duplicate"]}';
    END IF;

    IF p_first_name IS NULL THEN
        v_errors := v_errors || '{"firstName": ["required"]}';
    ELSIF char_length(p_first_name) > 100 THEN
        v_errors := v_errors || '{"firstName": ["maxLength"]}';
    END IF;
    IF p_last_name IS NULL THEN
        v_errors := v_errors || '{"lastName": ["required"]}';
    ELSIF char_length(p_last_name) > 100 THEN
        v_errors := v_errors || '{"lastName": ["maxLength"]}';
    END IF;

    IF p_pesel IS NULL AND p_identity_document IS NULL THEN
        v_errors := v_errors || '{"pesel": ["required"]}';
    ELSIF p_pesel IS NOT NULL AND NOT employees.pesel_valid(p_pesel) THEN
        v_errors := v_errors || '{"pesel": ["invalid"]}';
    ELSIF p_pesel IS NOT NULL AND EXISTS (
        SELECT FROM employees.employees e
        WHERE e.company_id = p_company_id AND e.pesel_hmac = employees.pesel_hash(p_pesel)
          AND e.id IS DISTINCT FROM p_id
    ) THEN
        v_errors := v_errors || '{"pesel": ["duplicate"]}';
    END IF;
    IF char_length(p_identity_document) > 100 THEN
        v_errors := v_errors || '{"identityDocument": ["maxLength"]}';
    END IF;

    IF p_birth_date IS NULL THEN
        v_errors := v_errors || '{"birthDate": ["required"]}';
    ELSIF p_pesel IS NOT NULL AND employees.pesel_valid(p_pesel)
          AND employees.pesel_birth_date(p_pesel) <> p_birth_date THEN
        v_errors := v_errors || '{"birthDate": ["mismatch"]}';
    END IF;

    -- Nieaktywny urząd wolno zachować (kartoteka już go ma), ale nie wybrać na nowo.
    IF p_tax_office_id IS NULL THEN
        v_errors := v_errors || '{"taxOfficeId": ["required"]}';
    ELSIF NOT EXISTS (SELECT FROM dictionaries.tax_offices t WHERE t.id = p_tax_office_id) THEN
        v_errors := v_errors || '{"taxOfficeId": ["notFound"]}';
    ELSIF NOT EXISTS (SELECT FROM dictionaries.tax_offices t WHERE t.id = p_tax_office_id AND t.is_active)
      AND NOT EXISTS (SELECT FROM employees.employees e WHERE e.id = p_id AND e.tax_office_id = p_tax_office_id) THEN
        v_errors := v_errors || '{"taxOfficeId": ["inactive"]}';
    END IF;

    IF char_length(p_email) > 200 THEN
        v_errors := v_errors || '{"email": ["maxLength"]}';
    ELSIF p_email IS NOT NULL AND p_email !~ '^[^@\s]+@[^@\s]+\.[^@\s]+$' THEN
        v_errors := v_errors || '{"email": ["invalid"]}';
    END IF;

    RETURN v_errors;
END;
$$;

--changeset hr:employees.address_validate runOnChange:true splitStatements:false
--comment: Błędy pól adresu.
CREATE OR REPLACE FUNCTION employees.address_validate(
    p_street text,
    p_postal_code text,
    p_city text,
    p_valid_from date
)
    RETURNS jsonb
    LANGUAGE plpgsql
    IMMUTABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_errors jsonb := '{}';
BEGIN
    IF p_street IS NULL THEN
        v_errors := v_errors || '{"street": ["required"]}';
    ELSIF char_length(p_street) > 200 THEN
        v_errors := v_errors || '{"street": ["maxLength"]}';
    END IF;
    IF p_postal_code IS NULL THEN
        v_errors := v_errors || '{"postalCode": ["required"]}';
    ELSIF p_postal_code !~ '^\d{2}-\d{3}$' THEN
        v_errors := v_errors || '{"postalCode": ["invalid"]}';
    END IF;
    IF p_city IS NULL THEN
        v_errors := v_errors || '{"city": ["required"]}';
    ELSIF char_length(p_city) > 100 THEN
        v_errors := v_errors || '{"city": ["maxLength"]}';
    END IF;
    IF p_valid_from IS NULL THEN
        v_errors := v_errors || '{"validFrom": ["required"]}';
    END IF;
    RETURN v_errors;
END;
$$;

--changeset hr:employees.audit_view runOnChange:true splitStatements:false
--comment: Stan kartoteki do audytu zmian: bez PESEL, dokumentu i rachunku (tylko informacja, czy są).
CREATE OR REPLACE FUNCTION employees.audit_view(p_row employees.employees)
    RETURNS jsonb
    LANGUAGE sql
    IMMUTABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT jsonb_build_object(
        'companyId', p_row.company_id,
        'number', p_row.number,
        'firstName', p_row.first_name,
        'secondName', p_row.second_name,
        'lastName', p_row.last_name,
        'hasPesel', p_row.pesel_enc IS NOT NULL,
        'hasIdentityDocument', p_row.identity_document_enc IS NOT NULL,
        'birthDate', p_row.birth_date,
        'taxOfficeId', p_row.tax_office_id,
        'email', p_row.email
    );
$$;
