--liquibase formatted sql

--changeset hr:employees.matching_ids runOnChange:true splitStatements:false
--comment: Kartoteki pasujące do filtra listy: spółka, status z umów, wyszukiwanie po nazwisku, imieniu, numerze,
--comment: akronimie, a po pełnym poprawnym PESEL dokładnie (HMAC) — ten sam warunek co w employee_list (kopiowanie
--comment: „według filtra” bierze dokładnie to, co pokazuje lista).
CREATE OR REPLACE FUNCTION employees.matching_ids(p_company_id uuid, p_search text, p_status text)
    RETURNS SETOF uuid
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_search text := nullif(btrim(p_search), '');
    v_like text := '%' || replace(replace(replace(v_search, '\', '\\'), '%', '\%'), '_', '\_') || '%';
    v_pesel_hmac bytea;
BEGIN
    IF employees.pesel_valid(platform.digits(v_search)) AND char_length(v_search) <= 13 THEN
        v_pesel_hmac := employees.pesel_hash(platform.digits(v_search));
    END IF;
    RETURN QUERY
    SELECT e.id
    FROM employees.employees e
    WHERE (p_company_id IS NULL OR e.company_id = p_company_id)
      AND (nullif(p_status, '') IS NULL OR employment.employee_status(e.id, current_date) = p_status)
      AND (
          v_search IS NULL
          OR (v_pesel_hmac IS NOT NULL AND e.pesel_hmac = v_pesel_hmac)
          OR (v_pesel_hmac IS NULL AND (
              e.last_name || ' ' || e.first_name ILIKE v_like
              OR e.first_name || ' ' || e.last_name ILIKE v_like
              OR e.number ILIKE v_like
              OR e.acronym ILIKE v_like
          ))
      );
END;
$$;

--changeset hr:employees_app.employee_copy runOnChange:true splitStatements:false
--comment: Kopiowanie kartotek do innej spółki (R18–R22): wskazane p_ids albo — gdy p_ids puste — wszystkie pasujące do
--comment: filtra listy. Kopia: dane osobowe, bieżący adres zamieszkania i rachunek od dziś, nowy numer i akronim;
--comment: powiązanie z kontem, gdy konto nie ma jeszcze profilu w spółce docelowej. Pominięte z powodem:
--comment: sameCompany, peselExists, personExists. Najwyżej 500 naraz (tooMany).
DROP FUNCTION IF EXISTS employees_app.employee_copy(uuid, uuid[], uuid, text, text);
CREATE FUNCTION employees_app.employee_copy(
    p_target_company_id uuid,
    p_ids uuid[],
    p_company_id uuid DEFAULT NULL,
    p_search text DEFAULT NULL,
    p_status text DEFAULT NULL
)
    RETURNS TABLE (source_id uuid, source_name text, new_id uuid, skipped_reason text)
    LANGUAGE plpgsql
    VOLATILE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_ids uuid[];
    v_source employees.employees%ROWTYPE;
    v_new employees.employees%ROWTYPE;
    v_reason text;
    v_id uuid;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    IF p_target_company_id IS NULL THEN
        PERFORM platform.fail_validation('{"targetCompanyId": ["required"]}');
    ELSIF NOT EXISTS (SELECT FROM platform.companies c WHERE c.id = p_target_company_id AND c.is_active) THEN
        PERFORM platform.fail_validation('{"targetCompanyId": ["notFound"]}');
    END IF;

    v_ids := CASE WHEN coalesce(cardinality(p_ids), 0) > 0 THEN p_ids
                  ELSE ARRAY(SELECT employees.matching_ids(p_company_id, p_search, p_status)) END;
    IF cardinality(v_ids) > 500 THEN
        PERFORM platform.fail_validation('{"employeeIds": ["tooMany"]}');
    END IF;
    -- Jedno nadawanie numerów naraz w spółce docelowej (jak przy zakładaniu kartoteki).
    PERFORM pg_advisory_xact_lock(hashtext('employees.number:' || p_target_company_id::text));

    FOREACH v_id IN ARRAY coalesce(v_ids, '{}')
    LOOP
        SELECT * INTO v_source FROM employees.employees e WHERE e.id = v_id;
        CONTINUE WHEN NOT FOUND;
        v_reason := CASE
            WHEN v_source.company_id = p_target_company_id THEN 'sameCompany'
            WHEN v_source.pesel_hmac IS NOT NULL AND EXISTS (
                SELECT FROM employees.employees e
                WHERE e.company_id = p_target_company_id AND e.pesel_hmac = v_source.pesel_hmac
            ) THEN 'peselExists'
            WHEN v_source.pesel_hmac IS NULL AND EXISTS (
                SELECT FROM employees.employees e
                WHERE e.company_id = p_target_company_id AND e.birth_date = v_source.birth_date
                  AND lower(e.first_name) = lower(v_source.first_name) AND lower(e.last_name) = lower(v_source.last_name)
            ) THEN 'personExists'
        END;
        IF v_reason IS NOT NULL THEN
            source_id := v_id; source_name := v_source.last_name || ' ' || v_source.first_name;
            new_id := NULL; skipped_reason := v_reason;
            RETURN NEXT;
            CONTINUE;
        END IF;

        -- Szyfrogramy PESEL i dokumentu przechodzą bez zmian: ten sam klucz, więc nie trzeba odszyfrowywać.
        INSERT INTO employees.employees (company_id, number, first_name, second_name, last_name, pesel_enc, pesel_hmac,
                                         identity_document_enc, birth_date, tax_office_id, email, created_by)
        VALUES (p_target_company_id,
                (SELECT (coalesce(max(e.number::bigint), 0) + 1)::text FROM employees.employees e
                 WHERE e.company_id = p_target_company_id AND e.number ~ '^\d{1,15}$'),
                v_source.first_name, v_source.second_name, v_source.last_name, v_source.pesel_enc, v_source.pesel_hmac,
                v_source.identity_document_enc, v_source.birth_date, v_source.tax_office_id, v_source.email,
                platform.actor_id())
        RETURNING * INTO v_new;

        INSERT INTO employees.addresses (employee_id, kind, street, postal_code, city, country, valid_from, created_by)
        SELECT v_new.id, a.kind, a.street, a.postal_code, a.city, a.country, current_date, platform.actor_id()
        FROM employees.addresses a
        WHERE a.employee_id = v_id AND a.kind = 'residence' AND a.valid_to IS NULL;

        INSERT INTO employees.bank_accounts (employee_id, iban_enc, iban_last4, valid_from, created_by)
        SELECT v_new.id, b.iban_enc, b.iban_last4, current_date, platform.actor_id()
        FROM employees.bank_accounts b
        WHERE b.employee_id = v_id AND b.valid_to IS NULL;

        INSERT INTO platform.user_employee_links (user_id, employee_id, company_id, linked_by)
        SELECT l.user_id, v_new.id, p_target_company_id, platform.actor_id()
        FROM platform.user_employee_links l
        WHERE l.employee_id = v_id
          AND NOT EXISTS (
              SELECT FROM platform.user_employee_links o
              WHERE o.user_id = l.user_id AND o.company_id = p_target_company_id
          );

        PERFORM platform.audit('employee', v_new.id::text, 'create', NULL,
            employees.audit_view(v_new) || jsonb_build_object('copiedFrom', v_id));
        source_id := v_id; source_name := v_source.last_name || ' ' || v_source.first_name;
        new_id := v_new.id; skipped_reason := NULL;
        RETURN NEXT;
    END LOOP;
END;
$$;
