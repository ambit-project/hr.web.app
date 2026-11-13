--liquibase formatted sql

--changeset hr:documents_app.pit11_generate runOnChange:true splitStatements:false
--comment: Generowanie PIT-11 spółki za rok (R7; Kadry, Administrator) dla wszystkich kartotek z wypłatą do PIT-11 albo
--comment: dla jednej (p_employee_id). Brak formularza → wersja 1 (filing). Ostatnia wersja niewysłana → zastąpiona
--comment: nową treścią (przekazanie podatnikowi kasowane), gdy treść się zmieniła. Ostatnia wysłana i zmiana treści →
--comment: nowa wersja z celem correction [D]. Bez zmian — nic. p_changed — liczba nowych i zmienionych formularzy.
--comment: Błędy: year required / invalid (poza 2024–2100), employeeId notFound (kartoteka spoza spółki) / noPayments
--comment: (brak wypłat do PIT-11 w roku). Brak PESEL, adresu albo urzędu — formularz powstaje z listą missing.
CREATE OR REPLACE PROCEDURE documents_app.pit11_generate(
    IN p_company_id uuid,
    IN p_year integer,
    IN p_employee_id uuid,
    INOUT p_changed integer
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_employee_id uuid;
    v_data jsonb;
    v_latest documents.tax_forms;
    v_id uuid;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    IF NOT EXISTS (SELECT FROM platform.companies c WHERE c.id = p_company_id) THEN
        RAISE EXCEPTION 'company not found' USING ERRCODE = 'U0002';
    END IF;
    IF p_year IS NULL THEN
        PERFORM platform.fail_validation('{"year": ["required"]}');
    ELSIF p_year NOT BETWEEN 2024 AND 2100 THEN
        PERFORM platform.fail_validation('{"year": ["invalid"]}');
    END IF;
    IF p_employee_id IS NOT NULL THEN
        IF NOT EXISTS (SELECT FROM employees.employees e WHERE e.id = p_employee_id AND e.company_id = p_company_id)
        THEN
            PERFORM platform.fail_validation('{"employeeId": ["notFound"]}');
        END IF;
        IF NOT EXISTS (SELECT FROM documents.pit11_employees(p_company_id, p_year) x WHERE x = p_employee_id) THEN
            PERFORM platform.fail_validation('{"employeeId": ["noPayments"]}');
        END IF;
    END IF;

    p_changed := 0;
    FOR v_employee_id IN
        SELECT x FROM documents.pit11_employees(p_company_id, p_year) x
        WHERE p_employee_id IS NULL OR x = p_employee_id
        ORDER BY x
    LOOP
        v_data := documents.pit11_compute(v_employee_id, p_year);
        SELECT * INTO v_latest FROM documents.tax_forms f
        WHERE f.employee_id = v_employee_id AND f.year = p_year
        ORDER BY f.version DESC
        LIMIT 1
        FOR UPDATE;

        IF v_latest.id IS NULL THEN
            INSERT INTO documents.tax_forms (company_id, employee_id, year, version, purpose, data, generated_by)
            VALUES (p_company_id, v_employee_id, p_year, 1, 'filing', v_data, platform.actor_id())
            RETURNING id INTO v_id;
            PERFORM platform.audit('tax_form', v_id::text, 'create', NULL,
                jsonb_build_object('employeeId', v_employee_id, 'year', p_year, 'version', 1, 'purpose', 'filing'));
            p_changed := p_changed + 1;
        ELSIF v_latest.data IS NOT DISTINCT FROM v_data THEN
            CONTINUE;
        ELSIF v_latest.sent_to_office_at IS NULL THEN
            UPDATE documents.tax_forms f
            SET data = v_data, generated_at = now(), generated_by = platform.actor_id(),
                given_to_employee_at = NULL, given_how = NULL
            WHERE f.id = v_latest.id;
            PERFORM platform.audit('tax_form', v_latest.id::text, 'update',
                jsonb_build_object('generatedAt', v_latest.generated_at,
                                   'givenToEmployeeAt', v_latest.given_to_employee_at),
                jsonb_build_object('operation', 'regenerate'));
            p_changed := p_changed + 1;
        ELSE
            INSERT INTO documents.tax_forms (company_id, employee_id, year, version, purpose, data, generated_by)
            VALUES (p_company_id, v_employee_id, p_year, v_latest.version + 1, 'correction', v_data,
                    platform.actor_id())
            RETURNING id INTO v_id;
            PERFORM platform.audit('tax_form', v_id::text, 'create', NULL,
                jsonb_build_object('employeeId', v_employee_id, 'year', p_year, 'version', v_latest.version + 1,
                                   'purpose', 'correction', 'correctsId', v_latest.id));
            p_changed := p_changed + 1;
        END IF;
    END LOOP;
END;
$$;

--changeset hr:documents.tax_form_for_write runOnChange:true splitStatements:false
--comment: Formularz do oznaczenia (Kadry, Administrator): istnieje (U0002), kompletny (U0003 missingData).
CREATE OR REPLACE FUNCTION documents.tax_form_for_write(p_id uuid)
    RETURNS documents.tax_forms
    LANGUAGE plpgsql
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_form documents.tax_forms;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    SELECT * INTO v_form FROM documents.tax_forms f WHERE f.id = p_id FOR UPDATE;
    IF v_form.id IS NULL THEN
        RAISE EXCEPTION 'tax form not found' USING ERRCODE = 'U0002';
    END IF;
    IF jsonb_array_length(v_form.data -> 'missing') > 0 THEN
        RAISE EXCEPTION 'tax form has missing data' USING ERRCODE = 'U0003', HINT = 'missingData';
    END IF;
    RETURN v_form;
END;
$$;

--changeset hr:documents_app.pit11_mark_sent runOnChange:true splitStatements:false
--comment: Oznaczenie wysłania PIT-11 do urzędu skarbowego (R7, R9; Kadry, Administrator). Tylko ostatnia wersja
--comment: (U0003 notLatest), raz (U0003 sent), kompletna (U0003 missingData). sentAt: required, future. Po
--comment: oznaczeniu zmiana treści to korekta.
CREATE OR REPLACE PROCEDURE documents_app.pit11_mark_sent(IN p_id uuid, IN p_sent_at date)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_form documents.tax_forms;
BEGIN
    v_form := documents.tax_form_for_write(p_id);
    IF EXISTS (SELECT FROM documents.tax_forms f
               WHERE f.employee_id = v_form.employee_id AND f.year = v_form.year AND f.version > v_form.version) THEN
        RAISE EXCEPTION 'not the latest version' USING ERRCODE = 'U0003', HINT = 'notLatest';
    END IF;
    IF v_form.sent_to_office_at IS NOT NULL THEN
        RAISE EXCEPTION 'already sent' USING ERRCODE = 'U0003', HINT = 'sent';
    END IF;
    IF p_sent_at IS NULL THEN
        PERFORM platform.fail_validation('{"sentAt": ["required"]}');
    ELSIF p_sent_at > current_date THEN
        PERFORM platform.fail_validation('{"sentAt": ["future"]}');
    END IF;
    UPDATE documents.tax_forms f SET sent_to_office_at = p_sent_at WHERE f.id = p_id;
    PERFORM platform.audit('tax_form', p_id::text, 'update', jsonb_build_object('sentToOfficeAt', NULL),
        jsonb_build_object('sentToOfficeAt', p_sent_at, 'operation', 'sent'));
END;
$$;

--changeset hr:documents_app.pit11_mark_given runOnChange:true splitStatements:false
--comment: Oznaczenie przekazania PIT-11 podatnikowi (R9; Kadry, Administrator): data i sposób (paper, email, portal);
--comment: można poprawić. Kompletny formularz (U0003 missingData). givenAt: required, future; givenHow: required,
--comment: invalid. Przekazany formularz widzi pracownik w „Moich dokumentach”.
CREATE OR REPLACE PROCEDURE documents_app.pit11_mark_given(IN p_id uuid, IN p_given_at date, IN p_given_how text)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_form documents.tax_forms;
    v_errors jsonb := '{}';
BEGIN
    v_form := documents.tax_form_for_write(p_id);
    IF p_given_at IS NULL THEN
        v_errors := v_errors || '{"givenAt": ["required"]}';
    ELSIF p_given_at > current_date THEN
        v_errors := v_errors || '{"givenAt": ["future"]}';
    END IF;
    IF p_given_how IS NULL THEN
        v_errors := v_errors || '{"givenHow": ["required"]}';
    ELSIF p_given_how NOT IN ('paper', 'email', 'portal') THEN
        v_errors := v_errors || '{"givenHow": ["invalid"]}';
    END IF;
    IF v_errors <> '{}' THEN
        PERFORM platform.fail_validation(v_errors);
    END IF;
    UPDATE documents.tax_forms f SET given_to_employee_at = p_given_at, given_how = p_given_how WHERE f.id = p_id;
    PERFORM platform.audit('tax_form', p_id::text, 'update',
        jsonb_build_object('givenToEmployeeAt', v_form.given_to_employee_at, 'givenHow', v_form.given_how),
        jsonb_build_object('givenToEmployeeAt', p_given_at, 'givenHow', p_given_how, 'operation', 'given'));
END;
$$;

--changeset hr:documents_app.tax_form_get runOnChange:true splitStatements:false
--comment: Formularz PIT-11 (R8, R10): treść (data) i PESEL podatnika z kartoteki (odszyfrowany przy odczycie),
--comment: wysłanie, przekazanie, czy to ostatnia wersja. Kadry i Administrator — każdy; Pracownik — tylko swój,
--comment: wysłany albo przekazany (inny → U0002). Audyt odczytu (tax_form).
DROP FUNCTION IF EXISTS documents_app.tax_form_get(uuid);
CREATE FUNCTION documents_app.tax_form_get(p_id uuid)
    RETURNS TABLE (
        id uuid,
        company_id uuid,
        employee_id uuid,
        employee_number text,
        year integer,
        version integer,
        purpose text,
        data jsonb,
        pesel text,
        generated_at timestamptz,
        sent_to_office_at date,
        given_to_employee_at date,
        given_how text,
        is_latest boolean
    )
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_form documents.tax_forms;
BEGIN
    SELECT * INTO v_form FROM documents.tax_forms f WHERE f.id = p_id;
    IF v_form.id IS NULL THEN
        PERFORM documents.require_employee_access(NULL);
        RAISE EXCEPTION 'tax form not found' USING ERRCODE = 'U0002';
    END IF;
    IF NOT documents.require_employee_access(v_form.employee_id)
        AND v_form.sent_to_office_at IS NULL AND v_form.given_to_employee_at IS NULL THEN
        RAISE EXCEPTION 'tax form not found' USING ERRCODE = 'U0002';
    END IF;
    PERFORM platform.read_audit(v_form.employee_id, 'tax_form', p_id::text);
    RETURN QUERY
    SELECT f.id, f.company_id, f.employee_id, e.number, f.year, f.version, f.purpose, f.data,
           employees.decrypt(e.pesel_enc), f.generated_at, f.sent_to_office_at, f.given_to_employee_at, f.given_how,
           NOT EXISTS (SELECT FROM documents.tax_forms g
                       WHERE g.employee_id = f.employee_id AND g.year = f.year AND g.version > f.version)
    FROM documents.tax_forms f
    JOIN employees.employees e ON e.id = f.employee_id
    WHERE f.id = p_id;
END;
$$;

--changeset hr:documents_app.tax_form_list runOnChange:true splitStatements:false
--comment: PIT-11 spółki za rok (R7, R9; Kadry, Administrator): kartoteki z wypłatą do PIT-11 albo z formularzem,
--comment: ostatnia wersja (pusta, gdy nie wygenerowano), liczba wersji, wysłanie, przekazanie, brakujące dane
--comment: i is_outdated — treść ostatniej wersji różni się od wyliczonej teraz (np. po korekcie listy płac).
DROP FUNCTION IF EXISTS documents_app.tax_form_list(uuid, integer);
CREATE FUNCTION documents_app.tax_form_list(p_company_id uuid, p_year integer)
    RETURNS TABLE (
        employee_id uuid,
        employee_number text,
        first_name text,
        last_name text,
        tax_form_id uuid,
        version integer,
        purpose text,
        generated_at timestamptz,
        sent_to_office_at date,
        given_to_employee_at date,
        given_how text,
        missing text[],
        is_outdated boolean
    )
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    IF NOT EXISTS (SELECT FROM platform.companies c WHERE c.id = p_company_id) THEN
        RAISE EXCEPTION 'company not found' USING ERRCODE = 'U0002';
    END IF;
    RETURN QUERY
    WITH people AS (
        SELECT x AS employee_id FROM documents.pit11_employees(p_company_id, p_year) x
        UNION
        SELECT f.employee_id FROM documents.tax_forms f WHERE f.company_id = p_company_id AND f.year = p_year
    )
    SELECT e.id, e.number, e.first_name, e.last_name, f.id, f.version, f.purpose, f.generated_at,
           f.sent_to_office_at, f.given_to_employee_at, f.given_how,
           coalesce(ARRAY(SELECT jsonb_array_elements_text(f.data -> 'missing')), '{}'),
           f.id IS NOT NULL AND f.data IS DISTINCT FROM documents.pit11_compute(e.id, p_year)
    FROM people p
    JOIN employees.employees e ON e.id = p.employee_id
    LEFT JOIN LATERAL (
        SELECT g.* FROM documents.tax_forms g
        WHERE g.employee_id = e.id AND g.year = p_year
        ORDER BY g.version DESC
        LIMIT 1
    ) f ON true
    ORDER BY lower(e.last_name), lower(e.first_name), e.number;
END;
$$;

--changeset hr:documents_app.my_tax_forms runOnChange:true splitStatements:false
--comment: PIT-11 kartoteki, wszystkie wersje od najnowszego roku i wersji (R10). Kadry i Administrator — każda
--comment: kartoteka, wszystkie wersje; Pracownik — tylko swoja, wersje wysłane albo przekazane [D].
DROP FUNCTION IF EXISTS documents_app.my_tax_forms(uuid);
CREATE FUNCTION documents_app.my_tax_forms(p_employee_id uuid)
    RETURNS TABLE (
        id uuid,
        year integer,
        version integer,
        purpose text,
        generated_at timestamptz,
        sent_to_office_at date,
        given_to_employee_at date,
        given_how text
    )
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_staff boolean;
BEGIN
    v_staff := documents.require_employee_access(p_employee_id);
    RETURN QUERY
    SELECT f.id, f.year, f.version, f.purpose, f.generated_at, f.sent_to_office_at, f.given_to_employee_at,
           f.given_how
    FROM documents.tax_forms f
    WHERE f.employee_id = p_employee_id
      AND (v_staff OR f.sent_to_office_at IS NOT NULL OR f.given_to_employee_at IS NOT NULL)
    ORDER BY f.year DESC, f.version DESC;
END;
$$;
