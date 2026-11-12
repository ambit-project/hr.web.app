--liquibase formatted sql

--changeset hr:employees_app.address_list runOnChange:true splitStatements:false
--comment: Adresy kartoteki z historią (Kadry, Administrator).
CREATE OR REPLACE FUNCTION employees_app.address_list(p_employee_id uuid)
    RETURNS TABLE (
        id uuid,
        kind text,
        street text,
        postal_code text,
        city text,
        valid_from date,
        valid_to date
    )
    LANGUAGE plpgsql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    RETURN QUERY
    SELECT a.id, a.kind, a.street, a.postal_code, a.city, a.valid_from, a.valid_to
    FROM employees.addresses a
    WHERE a.employee_id = p_employee_id
    ORDER BY a.kind DESC, a.valid_from DESC;
END;
$$;

--changeset hr:employees_app.address_add runOnChange:true splitStatements:false
--comment: Nowy adres od dnia (R6): zamyka poprzedni adres tego rodzaju dzień wcześniej; ta sama data poprawia adres.
CREATE OR REPLACE PROCEDURE employees_app.address_add(
    IN p_employee_id uuid,
    IN p_kind text,
    IN p_street text,
    IN p_postal_code text,
    IN p_city text,
    IN p_valid_from date,
    INOUT p_id uuid DEFAULT NULL
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_street text := nullif(btrim(p_street), '');
    v_postal_code text := nullif(btrim(p_postal_code), '');
    v_city text := nullif(btrim(p_city), '');
    v_last employees.addresses%ROWTYPE;
    v_errors jsonb;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    PERFORM FROM employees.employees e WHERE e.id = p_employee_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'employee not found' USING ERRCODE = 'U0002';
    END IF;

    v_errors := employees.address_validate(v_street, v_postal_code, v_city, p_valid_from);
    IF p_kind IS NULL OR p_kind NOT IN ('residence', 'correspondence') THEN
        v_errors := v_errors || '{"kind": ["invalid"]}';
    END IF;
    IF v_errors = '{}' THEN
        SELECT * INTO v_last FROM employees.addresses a
        WHERE a.employee_id = p_employee_id AND a.kind = p_kind
        ORDER BY a.valid_from DESC LIMIT 1;
        IF FOUND AND p_valid_from < v_last.valid_from THEN
            v_errors := v_errors || '{"validFrom": ["beforeCurrent"]}';
        END IF;
    END IF;
    IF v_errors <> '{}' THEN
        PERFORM platform.fail_validation(v_errors);
    END IF;

    IF v_last.id IS NOT NULL AND p_valid_from = v_last.valid_from THEN
        UPDATE employees.addresses a
        SET street = v_street, postal_code = v_postal_code, city = v_city
        WHERE a.id = v_last.id;
        p_id := v_last.id;
        PERFORM platform.audit('employee_address', p_id::text, 'update', to_jsonb(v_last),
            jsonb_build_object('street', v_street, 'postal_code', v_postal_code, 'city', v_city));
        RETURN;
    END IF;

    IF v_last.id IS NOT NULL AND (v_last.valid_to IS NULL OR v_last.valid_to >= p_valid_from) THEN
        UPDATE employees.addresses a SET valid_to = p_valid_from - 1 WHERE a.id = v_last.id;
    END IF;

    INSERT INTO employees.addresses (employee_id, kind, street, postal_code, city, valid_from, created_by)
    VALUES (p_employee_id, p_kind, v_street, v_postal_code, v_city, p_valid_from, platform.actor_id())
    RETURNING id INTO p_id;
    PERFORM platform.audit('employee_address', p_id::text, 'create', NULL,
        jsonb_build_object('employee_id', p_employee_id, 'kind', p_kind, 'street', v_street,
            'postal_code', v_postal_code, 'city', v_city, 'valid_from', p_valid_from));
END;
$$;

--changeset hr:employees_app.bank_account_list runOnChange:true splitStatements:false
--comment: Rachunki kartoteki z historią, zamaskowane (4 ostatnie cyfry); pełny numer tylko w employee_get.
CREATE OR REPLACE FUNCTION employees_app.bank_account_list(p_employee_id uuid)
    RETURNS TABLE (id uuid, last4 text, valid_from date, valid_to date)
    LANGUAGE plpgsql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    RETURN QUERY
    SELECT b.id, b.iban_last4, b.valid_from, b.valid_to
    FROM employees.bank_accounts b
    WHERE b.employee_id = p_employee_id
    ORDER BY b.valid_from DESC;
END;
$$;

--changeset hr:employees_app.bank_account_add runOnChange:true splitStatements:false
--comment: Rachunek do wypłaty od dnia (R7): zamyka poprzedni dzień wcześniej; ta sama data poprawia numer.
CREATE OR REPLACE PROCEDURE employees_app.bank_account_add(
    IN p_employee_id uuid,
    IN p_iban text,
    IN p_valid_from date,
    INOUT p_id uuid DEFAULT NULL
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_iban text := employees.iban_normalize(p_iban);
    v_last employees.bank_accounts%ROWTYPE;
    v_errors jsonb := '{}';
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    PERFORM FROM employees.employees e WHERE e.id = p_employee_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'employee not found' USING ERRCODE = 'U0002';
    END IF;

    IF v_iban IS NULL THEN
        v_errors := v_errors || '{"iban": ["required"]}';
    ELSIF NOT employees.iban_valid(v_iban) THEN
        v_errors := v_errors || '{"iban": ["invalid"]}';
    END IF;
    IF p_valid_from IS NULL THEN
        v_errors := v_errors || '{"validFrom": ["required"]}';
    ELSE
        SELECT * INTO v_last FROM employees.bank_accounts b
        WHERE b.employee_id = p_employee_id
        ORDER BY b.valid_from DESC LIMIT 1;
        IF FOUND AND p_valid_from < v_last.valid_from THEN
            v_errors := v_errors || '{"validFrom": ["beforeCurrent"]}';
        END IF;
    END IF;
    IF v_errors <> '{}' THEN
        PERFORM platform.fail_validation(v_errors);
    END IF;

    IF v_last.id IS NOT NULL AND p_valid_from = v_last.valid_from THEN
        UPDATE employees.bank_accounts b
        SET iban_enc = employees.encrypt(v_iban), iban_last4 = right(v_iban, 4)
        WHERE b.id = v_last.id;
        p_id := v_last.id;
        PERFORM platform.audit('employee_bank_account', p_id::text, 'update',
            jsonb_build_object('last4', v_last.iban_last4), jsonb_build_object('last4', right(v_iban, 4)));
        RETURN;
    END IF;

    IF v_last.id IS NOT NULL AND (v_last.valid_to IS NULL OR v_last.valid_to >= p_valid_from) THEN
        UPDATE employees.bank_accounts b SET valid_to = p_valid_from - 1 WHERE b.id = v_last.id;
    END IF;

    INSERT INTO employees.bank_accounts (employee_id, iban_enc, iban_last4, valid_from, created_by)
    VALUES (p_employee_id, employees.encrypt(v_iban), right(v_iban, 4), p_valid_from, platform.actor_id())
    RETURNING id INTO p_id;
    PERFORM platform.audit('employee_bank_account', p_id::text, 'create', NULL,
        jsonb_build_object('employee_id', p_employee_id, 'last4', right(v_iban, 4), 'valid_from', p_valid_from));
END;
$$;

--changeset hr:employees_app.bank_account_close runOnChange:true splitStatements:false
--comment: Zamknięcie bieżącego rachunku z datą (R7); od następnego dnia wypłata gotówką. Brak otwartego → U0003.
CREATE OR REPLACE PROCEDURE employees_app.bank_account_close(IN p_employee_id uuid, IN p_valid_to date)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_open employees.bank_accounts%ROWTYPE;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    SELECT * INTO v_open FROM employees.bank_accounts b
    WHERE b.employee_id = p_employee_id AND b.valid_to IS NULL
    FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'no open bank account' USING ERRCODE = 'U0003';
    END IF;
    IF p_valid_to IS NULL THEN
        PERFORM platform.fail_validation('{"validTo": ["required"]}');
    ELSIF p_valid_to < v_open.valid_from THEN
        PERFORM platform.fail_validation('{"validTo": ["beforeFrom"]}');
    END IF;

    UPDATE employees.bank_accounts b SET valid_to = p_valid_to WHERE b.id = v_open.id;
    PERFORM platform.audit('employee_bank_account', v_open.id::text, 'update',
        jsonb_build_object('valid_to', NULL), jsonb_build_object('valid_to', p_valid_to));
END;
$$;
