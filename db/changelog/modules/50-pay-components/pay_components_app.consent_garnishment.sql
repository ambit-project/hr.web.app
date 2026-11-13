--liquibase formatted sql

--changeset hr:pay_components_app.consent_list runOnChange:true splitStatements:false
--comment: Zgody kartoteki (R13, R16). Kadry i Administrator — każda kartoteka; Pracownik — tylko powiązana z kontem.
CREATE OR REPLACE FUNCTION pay_components_app.consent_list(p_employee_id uuid)
    RETURNS TABLE (
        id uuid,
        component_type_id uuid,
        code text,
        name text,
        monthly_amount numeric,
        valid_from date,
        valid_to date,
        signed_at date,
        status text,
        is_used boolean,
        row_version bigint
    )
    LANGUAGE plpgsql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    IF NOT EXISTS (
        SELECT FROM platform.user_roles r JOIN platform.users u ON u.id = r.user_id
        WHERE r.user_id = platform.actor_id() AND u.is_active AND r.role IN ('kadry', 'administrator')
    ) THEN
        PERFORM platform.require_role('pracownik');
        IF NOT EXISTS (
            SELECT FROM platform.user_employee_links l
            WHERE l.user_id = platform.actor_id() AND l.employee_id = p_employee_id
        ) THEN
            RAISE EXCEPTION 'forbidden' USING ERRCODE = 'U0004';
        END IF;
    END IF;
    IF NOT EXISTS (SELECT FROM employees.employees e WHERE e.id = p_employee_id) THEN
        RAISE EXCEPTION 'employee not found' USING ERRCODE = 'U0002';
    END IF;

    RETURN QUERY
    SELECT d.id, t.id, t.code, t.name, d.monthly_amount, d.valid_from, d.valid_to, d.signed_at,
           pay_components.consent_status(d.signed_at, d.valid_from, d.valid_to, current_date),
           EXISTS (SELECT FROM pay_components.employment_components c WHERE c.consent_id = d.id),
           d.xmin::text::bigint
    FROM pay_components.consents d
    JOIN pay_components.component_types t ON t.id = d.component_type_id
    WHERE d.employee_id = p_employee_id
    ORDER BY d.valid_from DESC, t.code;
END;
$$;

--changeset hr:pay_components.consent_validate runOnChange:true splitStatements:false
--comment: Błędy pól zgody (R13).
CREATE OR REPLACE FUNCTION pay_components.consent_validate(
    p_component_type_id uuid,
    p_monthly_amount numeric,
    p_valid_from date,
    p_valid_to date,
    p_signed_at date
)
    RETURNS jsonb
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_errors jsonb := '{}';
    v_category text;
BEGIN
    SELECT t.deduction_category INTO v_category FROM pay_components.component_types t WHERE t.id = p_component_type_id;
    IF p_component_type_id IS NULL THEN
        v_errors := v_errors || '{"componentTypeId": ["required"]}';
    ELSIF NOT FOUND OR NOT pay_components.requires_consent(v_category) THEN
        v_errors := v_errors || '{"componentTypeId": ["invalid"]}';
    END IF;
    IF p_monthly_amount IS NULL THEN
        v_errors := v_errors || '{"monthlyAmount": ["required"]}';
    ELSIF p_monthly_amount <= 0 OR p_monthly_amount <> round(p_monthly_amount, 2) OR p_monthly_amount >= 10000000000
    THEN
        v_errors := v_errors || '{"monthlyAmount": ["invalid"]}';
    END IF;
    IF p_valid_from IS NULL THEN
        v_errors := v_errors || '{"validFrom": ["required"]}';
    ELSIF p_valid_to < p_valid_from THEN
        v_errors := v_errors || '{"validTo": ["beforeFrom"]}';
    END IF;
    IF p_signed_at > current_date THEN
        v_errors := v_errors || '{"signedAt": ["future"]}';
    END IF;
    RETURN v_errors;
END;
$$;

--changeset hr:pay_components_app.consent_create runOnChange:true splitStatements:false
--comment: Nowa zgoda (R13). Pusta data podpisania = dokument przygotowany, zgoda jeszcze nie działa.
CREATE OR REPLACE PROCEDURE pay_components_app.consent_create(
    IN p_employee_id uuid,
    IN p_component_type_id uuid,
    IN p_monthly_amount numeric,
    IN p_valid_from date,
    IN p_valid_to date,
    IN p_signed_at date,
    INOUT p_id uuid
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_errors jsonb;
    v_row pay_components.consents%ROWTYPE;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    IF NOT EXISTS (SELECT FROM employees.employees e WHERE e.id = p_employee_id) THEN
        RAISE EXCEPTION 'employee not found' USING ERRCODE = 'U0002';
    END IF;
    v_errors := pay_components.consent_validate(p_component_type_id, p_monthly_amount, p_valid_from, p_valid_to,
        p_signed_at);
    IF v_errors <> '{}' THEN
        PERFORM platform.fail_validation(v_errors);
    END IF;

    INSERT INTO pay_components.consents (employee_id, component_type_id, monthly_amount, valid_from, valid_to,
                                         signed_at, created_by)
    VALUES (p_employee_id, p_component_type_id, p_monthly_amount, p_valid_from, p_valid_to, p_signed_at,
            platform.actor_id())
    RETURNING * INTO v_row;
    p_id := v_row.id;
    PERFORM platform.audit('deduction_consent', p_id::text, 'create', NULL, to_jsonb(v_row));
END;
$$;

--changeset hr:pay_components_app.consent_update runOnChange:true splitStatements:false
--comment: Zmiana niepodpisanej zgody, w tym podpisanie (R15). Podpisanej nie zmienia się (U0003 signed).
CREATE OR REPLACE PROCEDURE pay_components_app.consent_update(
    IN p_id uuid,
    IN p_row_version bigint,
    IN p_monthly_amount numeric,
    IN p_valid_from date,
    IN p_valid_to date,
    IN p_signed_at date
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_before pay_components.consents%ROWTYPE;
    v_after pay_components.consents%ROWTYPE;
    v_errors jsonb;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    SELECT * INTO v_before FROM pay_components.consents d WHERE d.id = p_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'consent not found' USING ERRCODE = 'U0002';
    END IF;
    IF (SELECT d.xmin::text::bigint FROM pay_components.consents d WHERE d.id = p_id) <> p_row_version THEN
        RAISE EXCEPTION 'consent changed' USING ERRCODE = 'U0003';
    END IF;
    IF v_before.signed_at IS NOT NULL THEN
        RAISE EXCEPTION 'signed consent is immutable' USING ERRCODE = 'U0003', HINT = 'signed';
    END IF;
    v_errors := pay_components.consent_validate(v_before.component_type_id, p_monthly_amount, p_valid_from,
        p_valid_to, p_signed_at);
    IF v_errors <> '{}' THEN
        PERFORM platform.fail_validation(v_errors);
    END IF;

    UPDATE pay_components.consents d
    SET monthly_amount = p_monthly_amount, valid_from = p_valid_from, valid_to = p_valid_to, signed_at = p_signed_at
    WHERE d.id = p_id
    RETURNING * INTO v_after;
    PERFORM platform.audit('deduction_consent', p_id::text, 'update', to_jsonb(v_before), to_jsonb(v_after));
END;
$$;

--changeset hr:pay_components_app.consent_end runOnChange:true splitStatements:false
--comment: Zakończenie podpisanej zgody datą albo cofnięcie (pusta data) (R15). Zgoda musi dalej pokrywać przypisane
--comment: składniki (consentInUse).
CREATE OR REPLACE PROCEDURE pay_components_app.consent_end(IN p_id uuid, IN p_row_version bigint, IN p_valid_to date)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_before pay_components.consents%ROWTYPE;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    SELECT * INTO v_before FROM pay_components.consents d WHERE d.id = p_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'consent not found' USING ERRCODE = 'U0002';
    END IF;
    IF (SELECT d.xmin::text::bigint FROM pay_components.consents d WHERE d.id = p_id) <> p_row_version THEN
        RAISE EXCEPTION 'consent changed' USING ERRCODE = 'U0003';
    END IF;
    IF v_before.signed_at IS NULL THEN
        PERFORM platform.fail_validation('{"validTo": ["invalid"]}');
    END IF;
    IF p_valid_to < v_before.valid_from THEN
        PERFORM platform.fail_validation('{"validTo": ["beforeFrom"]}');
    END IF;
    IF EXISTS (
        SELECT FROM pay_components.employment_components c
        WHERE c.consent_id = p_id
          AND NOT daterange(v_before.valid_from, p_valid_to, '[]') @> daterange(c.valid_from, c.valid_to, '[]')
    ) THEN
        RAISE EXCEPTION 'consent covers an assigned component' USING ERRCODE = 'U0003', HINT = 'consentInUse';
    END IF;

    UPDATE pay_components.consents d SET valid_to = p_valid_to WHERE d.id = p_id;
    PERFORM platform.audit('deduction_consent', p_id::text, 'update',
        jsonb_build_object('valid_to', v_before.valid_to), jsonb_build_object('valid_to', p_valid_to));
END;
$$;

--changeset hr:pay_components_app.consent_delete runOnChange:true splitStatements:false
--comment: Usunięcie zgody wprowadzonej przez pomyłkę (R15); wskazanej w składniku nie usuwa się (consentInUse), użytej
--comment: na zamkniętej liście płac — U0003 payrollClosed (009 R25).
CREATE OR REPLACE PROCEDURE pay_components_app.consent_delete(IN p_id uuid, IN p_row_version bigint)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_row pay_components.consents%ROWTYPE;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    SELECT * INTO v_row FROM pay_components.consents d WHERE d.id = p_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'consent not found' USING ERRCODE = 'U0002';
    END IF;
    IF (SELECT d.xmin::text::bigint FROM pay_components.consents d WHERE d.id = p_id) <> p_row_version THEN
        RAISE EXCEPTION 'consent changed' USING ERRCODE = 'U0003';
    END IF;
    IF EXISTS (SELECT FROM pay_components.employment_components c WHERE c.consent_id = p_id) THEN
        RAISE EXCEPTION 'consent is used' USING ERRCODE = 'U0003', HINT = 'consentInUse';
    END IF;
    PERFORM payroll.guard_used('consent', p_id);
    DELETE FROM pay_components.consents d WHERE d.id = p_id;
    PERFORM platform.audit('deduction_consent', p_id::text, 'delete', to_jsonb(v_row), NULL);
END;
$$;

--changeset hr:pay_components_app.garnishment_list runOnChange:true splitStatements:false
--comment: Zajęcia kartoteki: alimenty przed innymi, potem doręczenie (R21; Kadry, Administrator). Zapisuje audyt odczytu
--comment: (R22), więc VOLATILE. Kwota ściągnięta — suma potrąceń z zamkniętych list płac (009 R25).
CREATE OR REPLACE FUNCTION pay_components_app.garnishment_list(p_employee_id uuid)
    RETURNS TABLE (
        id uuid,
        kind text,
        authority_kind text,
        authority_name text,
        case_number text,
        received_at date,
        monthly_amount numeric,
        total_amount numeric,
        collected_amount numeric,
        status text,
        status_date date,
        note text,
        row_version bigint
    )
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    IF NOT EXISTS (SELECT FROM employees.employees e WHERE e.id = p_employee_id) THEN
        RAISE EXCEPTION 'employee not found' USING ERRCODE = 'U0002';
    END IF;
    PERFORM platform.read_audit(p_employee_id, 'garnishment', p_employee_id::text);

    RETURN QUERY
    SELECT g.id, g.kind, g.authority_kind, g.authority_name, g.case_number, g.received_at, g.monthly_amount,
           g.total_amount, payroll.garnishment_collected(g.id), g.status, g.status_date, g.note, g.xmin::text::bigint
    FROM pay_components.garnishments g
    WHERE g.employee_id = p_employee_id
    ORDER BY g.status <> 'active', g.kind <> 'alimony', g.received_at, g.case_number;
END;
$$;

--changeset hr:pay_components_app.garnishment_create runOnChange:true splitStatements:false
--comment: Nowe zajęcie wynagrodzenia (R17, R18).
CREATE OR REPLACE PROCEDURE pay_components_app.garnishment_create(
    IN p_employee_id uuid,
    IN p_kind text,
    IN p_authority_kind text,
    IN p_authority_name text,
    IN p_case_number text,
    IN p_received_at date,
    IN p_monthly_amount numeric,
    IN p_total_amount numeric,
    IN p_note text,
    INOUT p_id uuid
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_authority text := nullif(btrim(p_authority_name), '');
    v_case text := nullif(btrim(p_case_number), '');
    v_note text := nullif(btrim(p_note), '');
    v_errors jsonb;
    v_row pay_components.garnishments%ROWTYPE;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    PERFORM FROM employees.employees e WHERE e.id = p_employee_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'employee not found' USING ERRCODE = 'U0002';
    END IF;
    v_errors := pay_components.garnishment_validate(NULL, p_employee_id, p_kind, p_authority_kind, v_authority,
        v_case, p_received_at, p_monthly_amount, p_total_amount, v_note);
    IF v_errors <> '{}' THEN
        PERFORM platform.fail_validation(v_errors);
    END IF;

    INSERT INTO pay_components.garnishments (employee_id, kind, authority_kind, authority_name, case_number,
                                             received_at, monthly_amount, total_amount, note, created_by)
    VALUES (p_employee_id, p_kind, p_authority_kind, v_authority, v_case, p_received_at, p_monthly_amount,
            p_total_amount, v_note, platform.actor_id())
    RETURNING * INTO v_row;
    p_id := v_row.id;
    PERFORM platform.audit('garnishment', p_id::text, 'create', NULL, to_jsonb(v_row));
END;
$$;

--changeset hr:pay_components_app.garnishment_update runOnChange:true splitStatements:false
--comment: Zmiana danych zajęcia (R17–R19), z wersją wiersza.
CREATE OR REPLACE PROCEDURE pay_components_app.garnishment_update(
    IN p_id uuid,
    IN p_row_version bigint,
    IN p_kind text,
    IN p_authority_kind text,
    IN p_authority_name text,
    IN p_case_number text,
    IN p_received_at date,
    IN p_monthly_amount numeric,
    IN p_total_amount numeric,
    IN p_note text
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_authority text := nullif(btrim(p_authority_name), '');
    v_case text := nullif(btrim(p_case_number), '');
    v_note text := nullif(btrim(p_note), '');
    v_before pay_components.garnishments%ROWTYPE;
    v_after pay_components.garnishments%ROWTYPE;
    v_errors jsonb;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    SELECT * INTO v_before FROM pay_components.garnishments g WHERE g.id = p_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'garnishment not found' USING ERRCODE = 'U0002';
    END IF;
    IF (SELECT g.xmin::text::bigint FROM pay_components.garnishments g WHERE g.id = p_id) <> p_row_version THEN
        RAISE EXCEPTION 'garnishment changed' USING ERRCODE = 'U0003';
    END IF;
    v_errors := pay_components.garnishment_validate(p_id, v_before.employee_id, p_kind, p_authority_kind, v_authority,
        v_case, p_received_at, p_monthly_amount, p_total_amount, v_note);
    IF v_errors = '{}' AND v_before.status_date < p_received_at THEN
        v_errors := '{"receivedAt": ["afterStatus"]}';
    END IF;
    IF v_errors <> '{}' THEN
        PERFORM platform.fail_validation(v_errors);
    END IF;

    UPDATE pay_components.garnishments g
    SET kind = p_kind, authority_kind = p_authority_kind, authority_name = v_authority, case_number = v_case,
        received_at = p_received_at, monthly_amount = p_monthly_amount, total_amount = p_total_amount, note = v_note
    WHERE g.id = p_id
    RETURNING * INTO v_after;
    PERFORM platform.audit('garnishment', p_id::text, 'update', to_jsonb(v_before), to_jsonb(v_after));
END;
$$;

--changeset hr:pay_components_app.garnishment_set_status runOnChange:true splitStatements:false
--comment: Status zajęcia: active, paidOff, discontinued z datą nie wcześniejszą niż doręczenie (R19).
CREATE OR REPLACE PROCEDURE pay_components_app.garnishment_set_status(
    IN p_id uuid,
    IN p_row_version bigint,
    IN p_status text,
    IN p_status_date date
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_before pay_components.garnishments%ROWTYPE;
    v_date date := CASE WHEN p_status = 'active' THEN NULL ELSE p_status_date END;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    SELECT * INTO v_before FROM pay_components.garnishments g WHERE g.id = p_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'garnishment not found' USING ERRCODE = 'U0002';
    END IF;
    IF (SELECT g.xmin::text::bigint FROM pay_components.garnishments g WHERE g.id = p_id) <> p_row_version THEN
        RAISE EXCEPTION 'garnishment changed' USING ERRCODE = 'U0003';
    END IF;
    IF p_status IS NULL OR p_status NOT IN ('active', 'paidOff', 'discontinued') THEN
        PERFORM platform.fail_validation('{"status": ["invalid"]}');
    END IF;
    IF p_status <> 'active' AND p_status_date IS NULL THEN
        PERFORM platform.fail_validation('{"statusDate": ["required"]}');
    ELSIF v_date < v_before.received_at THEN
        PERFORM platform.fail_validation('{"statusDate": ["beforeFrom"]}');
    ELSIF v_date > current_date THEN
        PERFORM platform.fail_validation('{"statusDate": ["future"]}');
    END IF;

    UPDATE pay_components.garnishments g SET status = p_status, status_date = v_date WHERE g.id = p_id;
    PERFORM platform.audit('garnishment', p_id::text, 'update',
        jsonb_build_object('status', v_before.status, 'status_date', v_before.status_date),
        jsonb_build_object('status', p_status, 'status_date', v_date));
END;
$$;

--changeset hr:pay_components_app.garnishment_delete runOnChange:true splitStatements:false
--comment: Usunięcie zajęcia wprowadzonego przez pomyłkę (R19). Potrącone na zamkniętej liście płac — U0003
--comment: payrollClosed (009 R25).
CREATE OR REPLACE PROCEDURE pay_components_app.garnishment_delete(IN p_id uuid, IN p_row_version bigint)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_row pay_components.garnishments%ROWTYPE;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    SELECT * INTO v_row FROM pay_components.garnishments g WHERE g.id = p_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'garnishment not found' USING ERRCODE = 'U0002';
    END IF;
    IF (SELECT g.xmin::text::bigint FROM pay_components.garnishments g WHERE g.id = p_id) <> p_row_version THEN
        RAISE EXCEPTION 'garnishment changed' USING ERRCODE = 'U0003';
    END IF;
    PERFORM payroll.guard_used('garnishment', p_id);
    DELETE FROM pay_components.garnishments g WHERE g.id = p_id;
    PERFORM platform.audit('garnishment', p_id::text, 'delete', to_jsonb(v_row), NULL);
END;
$$;
