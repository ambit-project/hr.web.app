--liquibase formatted sql

--changeset hr:employees_app.employee_list runOnChange:true splitStatements:false
--comment: Lista kartotek (Kadry, Administrator): filtr spółki, wyszukiwanie po nazwisku, imieniu, numerze, akronimie, a po pełnym
--comment: poprawnym PESEL dokładnie (HMAC), filtr statusu z umów (004 R16). PESEL tylko maskowany (5 cyfr), bez rachunku.
DROP FUNCTION IF EXISTS employees_app.employee_list(uuid, text, integer, integer);
DROP FUNCTION IF EXISTS employees_app.employee_list(uuid, text, integer, integer, text);
CREATE FUNCTION employees_app.employee_list(
    p_company_id uuid DEFAULT NULL,
    p_search text DEFAULT NULL,
    p_limit integer DEFAULT 50,
    p_offset integer DEFAULT 0,
    p_status text DEFAULT NULL
)
    RETURNS TABLE (
        id uuid,
        company_id uuid,
        company_short_name text,
        number text,
        acronym text,
        first_name text,
        second_name text,
        last_name text,
        birth_date date,
        city text,
        account_login text,
        status text,
        total_count bigint,
        pesel_masked text
    )
    LANGUAGE plpgsql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_search text := nullif(btrim(p_search), '');
    v_like text := '%' || replace(replace(replace(v_search, '\', '\\'), '%', '\%'), '_', '\_') || '%';
    v_pesel_hmac bytea;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    IF employees.pesel_valid(platform.digits(v_search)) AND char_length(v_search) <= 13 THEN
        v_pesel_hmac := employees.pesel_hash(platform.digits(v_search));
    END IF;

    RETURN QUERY
    SELECT e.id, e.company_id, c.short_name, e.number, e.acronym, e.first_name, e.second_name, e.last_name,
           e.birth_date, a.city, u.login, s.status, count(*) OVER (),
           -- Na liście tylko pierwsze 5 cyfr (decyzja właściciela 2026-11-13): bez audytu odczytu; pełny PESEL
           -- wyłącznie w szczegółach kartoteki, z audytem.
           CASE WHEN e.pesel_enc IS NOT NULL THEN left(employees.decrypt(e.pesel_enc), 5) || '******' END
    FROM employees.employees e
    CROSS JOIN LATERAL (SELECT employment.employee_status(e.id, current_date) AS status) s
    JOIN platform.companies c ON c.id = e.company_id
    LEFT JOIN employees.addresses a
        ON a.employee_id = e.id AND a.kind = 'residence' AND a.valid_to IS NULL
    LEFT JOIN platform.user_employee_links l ON l.employee_id = e.id
    LEFT JOIN platform.users u ON u.id = l.user_id
    WHERE (p_company_id IS NULL OR e.company_id = p_company_id)
      AND (nullif(p_status, '') IS NULL OR s.status = p_status)
      AND (
          v_search IS NULL
          OR (v_pesel_hmac IS NOT NULL AND e.pesel_hmac = v_pesel_hmac)
          OR (v_pesel_hmac IS NULL AND (
              e.last_name || ' ' || e.first_name ILIKE v_like
              OR e.first_name || ' ' || e.last_name ILIKE v_like
              OR e.number ILIKE v_like
              OR e.acronym ILIKE v_like
          ))
      )
    ORDER BY e.last_name COLLATE "pl-x-icu", e.first_name COLLATE "pl-x-icu", c.short_name, e.id
    LIMIT least(greatest(p_limit, 1), 100)
    OFFSET greatest(p_offset, 0);
END;
$$;

--changeset hr:employees_app.employee_get runOnChange:true splitStatements:false
--comment: Szczegóły kartoteki z odszyfrowanym PESEL, dokumentem i bieżącym rachunkiem (Kadry, Administrator).
--comment: Zapisuje audyt odczytu (R9), więc funkcja jest VOLATILE. Status i okres przechowywania z umów (004 R16, R17):
--comment: retention_open = jest umowa bez daty zakończenia.
DROP FUNCTION IF EXISTS employees_app.employee_get(uuid);
CREATE FUNCTION employees_app.employee_get(p_id uuid)
    RETURNS TABLE (
        id uuid,
        company_id uuid,
        company_short_name text,
        number text,
        acronym text,
        first_name text,
        second_name text,
        last_name text,
        pesel text,
        identity_document text,
        birth_date date,
        tax_office_id uuid,
        tax_office_code text,
        tax_office_name text,
        email text,
        iban text,
        account_login text,
        status text,
        retention_until date,
        retention_open boolean,
        row_version bigint
    )
    LANGUAGE plpgsql
    VOLATILE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    IF NOT EXISTS (SELECT FROM employees.employees e WHERE e.id = p_id) THEN
        RAISE EXCEPTION 'employee not found' USING ERRCODE = 'U0002';
    END IF;
    PERFORM platform.read_audit(p_id, 'employee', p_id::text);

    RETURN QUERY
    SELECT e.id, e.company_id, c.short_name, e.number, e.acronym, e.first_name, e.second_name, e.last_name,
           employees.decrypt(e.pesel_enc), employees.decrypt(e.identity_document_enc), e.birth_date,
           e.tax_office_id, t.code, t.name, e.email,
           (SELECT employees.decrypt(b.iban_enc) FROM employees.bank_accounts b
            WHERE b.employee_id = e.id AND b.valid_to IS NULL),
           u.login, employment.employee_status(e.id, current_date),
           nullif(r.until, 'infinity'), coalesce(r.until = 'infinity', false), e.xmin::text::bigint
    FROM employees.employees e
    CROSS JOIN LATERAL (SELECT employment.employee_retention_until(e.id) AS until) r
    JOIN platform.companies c ON c.id = e.company_id
    LEFT JOIN dictionaries.tax_offices t ON t.id = e.tax_office_id
    LEFT JOIN platform.user_employee_links l ON l.employee_id = e.id
    LEFT JOIN platform.users u ON u.id = l.user_id
    WHERE e.id = p_id;
END;
$$;

--changeset hr:employees_app.employee_create runOnChange:true splitStatements:false
--comment: Nowa kartoteka z adresem zamieszkania (Kadry, Administrator). Pusty numer = kolejny wolny numer w spółce.
CREATE OR REPLACE PROCEDURE employees_app.employee_create(
    IN p_company_id uuid,
    IN p_number text,
    IN p_first_name text,
    IN p_second_name text,
    IN p_last_name text,
    IN p_pesel text,
    IN p_identity_document text,
    IN p_birth_date date,
    IN p_tax_office_id uuid,
    IN p_email text,
    IN p_street text,
    IN p_postal_code text,
    IN p_city text,
    IN p_address_from date,
    INOUT p_id uuid DEFAULT NULL
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_row employees.employees%ROWTYPE;
    v_pesel text := platform.digits(p_pesel);
    v_document text := nullif(btrim(p_identity_document), '');
    v_errors jsonb := '{}';
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');

    IF p_company_id IS NULL THEN
        v_errors := v_errors || '{"companyId": ["required"]}';
    ELSIF NOT EXISTS (SELECT FROM platform.companies c WHERE c.id = p_company_id AND c.is_active) THEN
        v_errors := v_errors || '{"companyId": ["notFound"]}';
    ELSE
        -- Jedno nadanie numeru naraz w spółce: dwa równoległe dostałyby ten sam kolejny numer.
        PERFORM pg_advisory_xact_lock(hashtext('employees.number:' || p_company_id::text));
    END IF;

    v_row.company_id := p_company_id;
    v_row.number := nullif(btrim(p_number), '');
    IF v_row.number IS NULL AND p_company_id IS NOT NULL THEN
        SELECT (coalesce(max(e.number::bigint), 0) + 1)::text INTO v_row.number
        FROM employees.employees e
        WHERE e.company_id = p_company_id AND e.number ~ '^\d{1,15}$';
    END IF;
    v_row.first_name := nullif(btrim(p_first_name), '');
    v_row.second_name := nullif(btrim(p_second_name), '');
    v_row.last_name := nullif(btrim(p_last_name), '');
    v_row.birth_date := p_birth_date;
    v_row.tax_office_id := p_tax_office_id;
    v_row.email := nullif(btrim(p_email), '');

    v_errors := v_errors || employees.employee_validate(NULL, p_company_id, v_row.number, v_row.first_name,
        v_row.last_name, v_pesel, v_document, v_row.birth_date, v_row.tax_office_id, v_row.email)
        || employees.address_validate(nullif(btrim(p_street), ''), nullif(btrim(p_postal_code), ''),
            nullif(btrim(p_city), ''), p_address_from);
    IF v_errors <> '{}' THEN
        PERFORM platform.fail_validation(v_errors);
    END IF;

    INSERT INTO employees.employees (company_id, number, first_name, second_name, last_name, pesel_enc, pesel_hmac,
                                     identity_document_enc, birth_date, tax_office_id, email, created_by)
    VALUES (v_row.company_id, v_row.number, v_row.first_name, v_row.second_name, v_row.last_name,
            employees.encrypt(v_pesel), employees.pesel_hash(v_pesel), employees.encrypt(v_document),
            v_row.birth_date, v_row.tax_office_id, v_row.email, platform.actor_id())
    RETURNING * INTO v_row;
    p_id := v_row.id;

    INSERT INTO employees.addresses (employee_id, kind, street, postal_code, city, valid_from, created_by)
    VALUES (p_id, 'residence', btrim(p_street), btrim(p_postal_code), btrim(p_city), p_address_from,
            platform.actor_id());

    PERFORM platform.audit('employee', p_id::text, 'create', NULL, employees.audit_view(v_row));
EXCEPTION
    WHEN unique_violation THEN
        PERFORM platform.fail_validation('{"number": ["duplicate"]}');
END;
$$;

--changeset hr:employees_app.employee_update runOnChange:true splitStatements:false
--comment: Edycja kartoteki (Kadry, Administrator) z wersją wiersza, w tym akronimu. Spółki nie zmienia się. PESEL w audycie tylko jako
--comment: informacja o zmianie.
DROP PROCEDURE IF EXISTS employees_app.employee_update(uuid, bigint, text, text, text, text, text, text, date, uuid, text);
CREATE PROCEDURE employees_app.employee_update(
    IN p_id uuid,
    IN p_row_version bigint,
    IN p_number text,
    IN p_acronym text,
    IN p_first_name text,
    IN p_second_name text,
    IN p_last_name text,
    IN p_pesel text,
    IN p_identity_document text,
    IN p_birth_date date,
    IN p_tax_office_id uuid,
    IN p_email text
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_before employees.employees%ROWTYPE;
    v_after employees.employees%ROWTYPE;
    v_pesel text := platform.digits(p_pesel);
    v_document text := nullif(btrim(p_identity_document), '');
    v_errors jsonb;
    v_after_view jsonb;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');

    SELECT * INTO v_before FROM employees.employees e WHERE e.id = p_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'employee not found' USING ERRCODE = 'U0002';
    END IF;
    IF (SELECT e.xmin::text::bigint FROM employees.employees e WHERE e.id = p_id) <> p_row_version THEN
        RAISE EXCEPTION 'employee changed' USING ERRCODE = 'U0003';
    END IF;

    v_after := v_before;
    v_after.number := nullif(btrim(p_number), '');
    v_after.acronym := nullif(btrim(p_acronym), '');
    v_after.first_name := nullif(btrim(p_first_name), '');
    v_after.second_name := nullif(btrim(p_second_name), '');
    v_after.last_name := nullif(btrim(p_last_name), '');
    v_after.birth_date := p_birth_date;
    v_after.tax_office_id := p_tax_office_id;
    v_after.email := nullif(btrim(p_email), '');

    v_errors := employees.employee_validate(p_id, v_before.company_id, v_after.number, v_after.first_name,
        v_after.last_name, v_pesel, v_document, v_after.birth_date, v_after.tax_office_id, v_after.email);
    -- Akronim: wymagany, litery, cyfry, kropka, myślnik i podkreślnik; unikalny w spółce bez względu na wielkość liter.
    IF v_after.acronym IS NULL THEN
        v_errors := v_errors || '{"acronym": ["required"]}';
    ELSIF v_after.acronym !~ '^[A-Za-z0-9._-]{1,30}$' THEN
        v_errors := v_errors || '{"acronym": ["invalid"]}';
    ELSIF EXISTS (
        SELECT FROM employees.employees e
        WHERE e.company_id = v_before.company_id AND e.id <> p_id AND upper(e.acronym) = upper(v_after.acronym)
    ) THEN
        v_errors := v_errors || '{"acronym": ["duplicate"]}';
    END IF;
    IF v_errors <> '{}' THEN
        PERFORM platform.fail_validation(v_errors);
    END IF;

    v_after.pesel_hmac := employees.pesel_hash(v_pesel);
    -- Ten sam PESEL nie jest szyfrowany na nowo: szyfrogram z losową solą i tak by się zmienił, a audyt pokazałby
    -- zmianę, której nie było.
    IF v_after.pesel_hmac IS DISTINCT FROM v_before.pesel_hmac THEN
        v_after.pesel_enc := employees.encrypt(v_pesel);
    END IF;
    IF v_document IS DISTINCT FROM employees.decrypt(v_before.identity_document_enc) THEN
        v_after.identity_document_enc := employees.encrypt(v_document);
    END IF;

    UPDATE employees.employees e
    SET number = v_after.number, acronym = v_after.acronym, first_name = v_after.first_name, second_name = v_after.second_name,
        last_name = v_after.last_name, pesel_enc = v_after.pesel_enc, pesel_hmac = v_after.pesel_hmac,
        identity_document_enc = v_after.identity_document_enc, birth_date = v_after.birth_date,
        tax_office_id = v_after.tax_office_id, email = v_after.email
    WHERE e.id = p_id;

    v_after_view := employees.audit_view(v_after)
        || jsonb_build_object(
            'peselChanged', v_after.pesel_hmac IS DISTINCT FROM v_before.pesel_hmac,
            'identityDocumentChanged', v_after.identity_document_enc IS DISTINCT FROM v_before.identity_document_enc
        );
    PERFORM platform.audit('employee', p_id::text, 'update', employees.audit_view(v_before), v_after_view);
EXCEPTION
    WHEN unique_violation THEN
        PERFORM platform.fail_validation('{"number": ["duplicate"]}');
END;
$$;

--changeset hr:employees_app.employee_delete runOnChange:true splitStatements:false
--comment: Usunięcie kartoteki (R11). Z umowami — dopiero po upływie okresu przechowywania wszystkich umów, inaczej
--comment: U0003 z powodem retention (004 R18); umowy znikają razem z kartoteką.
CREATE OR REPLACE PROCEDURE employees_app.employee_delete(IN p_id uuid)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_row employees.employees%ROWTYPE;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    SELECT * INTO v_row FROM employees.employees e WHERE e.id = p_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'employee not found' USING ERRCODE = 'U0002';
    END IF;
    IF employment.employee_retention_until(p_id) >= current_date THEN
        RAISE EXCEPTION 'employee records are within the retention period'
            USING ERRCODE = 'U0003', HINT = 'retention';
    END IF;

    DELETE FROM employees.employees e WHERE e.id = p_id;
    -- Bez danych osobowych: kartoteki już nie ma, a wpis audytu zostaje na zawsze.
    PERFORM platform.audit('employee', p_id::text, 'delete',
        jsonb_build_object('companyId', v_row.company_id, 'number', v_row.number), NULL);
END;
$$;
