--liquibase formatted sql

--changeset hr:employment_app.employment_list runOnChange:true splitStatements:false
--comment: Umowy kartoteki ze statusem, okresem przechowywania i bieżącą wersją warunków bez kwot (Kadry, Administrator).
--comment: Kwoty są tylko w terms_list, którego odczyt trafia do audytu.
CREATE OR REPLACE FUNCTION employment_app.employment_list(p_employee_id uuid)
    RETURNS TABLE (
        id uuid,
        number text,
        kind text,
        contract_type text,
        signed_at date,
        start_date date,
        end_date date,
        terminated_at date,
        status text,
        retention_until date,
        position_name text,
        fte_numerator integer,
        fte_denominator integer,
        pay_basis text
    )
    LANGUAGE plpgsql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    IF NOT EXISTS (SELECT FROM employees.employees e WHERE e.id = p_employee_id) THEN
        RAISE EXCEPTION 'employee not found' USING ERRCODE = 'U0002';
    END IF;

    RETURN QUERY
    SELECT m.id, m.number, m.kind, m.contract_type, m.signed_at, m.start_date, m.end_date, m.terminated_at,
           employment.status(m.start_date, m.end_date, m.terminated_at, current_date),
           nullif(employment.retention_until(m.kind, m.start_date, m.end_date, m.terminated_at), 'infinity'),
           p.name, t.fte_numerator, t.fte_denominator, t.pay_basis
    FROM employment.employments m
    LEFT JOIN LATERAL (
        SELECT * FROM employment.terms t
        WHERE t.employment_id = m.id AND t.valid_from <= greatest(current_date, m.start_date)
        ORDER BY t.valid_from DESC LIMIT 1
    ) t ON true
    LEFT JOIN dictionaries.positions p ON p.id = t.position_id
    WHERE m.employee_id = p_employee_id
    ORDER BY m.start_date DESC, m.number;
END;
$$;

--changeset hr:employment_app.employment_get runOnChange:true splitStatements:false
--comment: Umowa z wersją wiersza, kartoteką i spółką (Kadry, Administrator).
CREATE OR REPLACE FUNCTION employment_app.employment_get(p_id uuid)
    RETURNS TABLE (
        id uuid,
        employee_id uuid,
        employee_name text,
        company_short_name text,
        number text,
        kind text,
        contract_type text,
        signed_at date,
        start_date date,
        end_date date,
        terminated_at date,
        status text,
        retention_until date,
        row_version bigint
    )
    LANGUAGE plpgsql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    IF NOT EXISTS (SELECT FROM employment.employments m WHERE m.id = p_id) THEN
        RAISE EXCEPTION 'employment not found' USING ERRCODE = 'U0002';
    END IF;

    RETURN QUERY
    SELECT m.id, m.employee_id, e.last_name || ' ' || e.first_name, c.short_name, m.number, m.kind, m.contract_type,
           m.signed_at, m.start_date, m.end_date, m.terminated_at,
           employment.status(m.start_date, m.end_date, m.terminated_at, current_date),
           nullif(employment.retention_until(m.kind, m.start_date, m.end_date, m.terminated_at), 'infinity'),
           m.xmin::text::bigint
    FROM employment.employments m
    JOIN employees.employees e ON e.id = m.employee_id
    JOIN platform.companies c ON c.id = m.company_id
    WHERE m.id = p_id;
END;
$$;

--changeset hr:employment_app.terms_list runOnChange:true splitStatements:false
--comment: Wszystkie wersje warunków umowy z kwotami (Kadry, Administrator). Zapisuje audyt odczytu (R19), więc VOLATILE.
CREATE OR REPLACE FUNCTION employment_app.terms_list(p_employment_id uuid)
    RETURNS TABLE (
        id uuid,
        valid_from date,
        valid_to date,
        position_id uuid,
        position_name text,
        fte_numerator integer,
        fte_denominator integer,
        pay_basis text,
        base_amount numeric,
        zus_title_code text,
        zus_pension_code text,
        zus_disability_code text,
        insurances text[],
        cost_kind text
    )
    LANGUAGE plpgsql
    VOLATILE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_employee_id uuid;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    SELECT m.employee_id INTO v_employee_id FROM employment.employments m WHERE m.id = p_employment_id;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'employment not found' USING ERRCODE = 'U0002';
    END IF;
    PERFORM platform.read_audit(v_employee_id, 'employment_terms', p_employment_id::text);

    RETURN QUERY
    SELECT t.id, t.valid_from, t.valid_to, t.position_id, p.name, t.fte_numerator, t.fte_denominator, t.pay_basis,
           t.base_amount, zt.code, zp.code, zd.code, t.insurances, t.cost_kind
    FROM employment.terms t
    LEFT JOIN dictionaries.positions p ON p.id = t.position_id
    LEFT JOIN dictionaries.zus_codes zt ON zt.id = t.zus_title_code_id
    LEFT JOIN dictionaries.zus_codes zp ON zp.id = t.zus_pension_code_id
    LEFT JOIN dictionaries.zus_codes zd ON zd.id = t.zus_disability_code_id
    WHERE t.employment_id = p_employment_id
    ORDER BY t.valid_from DESC;
END;
$$;

--changeset hr:employment_app.employment_create runOnChange:true splitStatements:false
--comment: Nowa umowa z pierwszą wersją warunków od początku umowy (R1–R5, R10–R13). Pusty numer = kolejny w spółce.
CREATE OR REPLACE PROCEDURE employment_app.employment_create(
    IN p_employee_id uuid,
    IN p_number text,
    IN p_kind text,
    IN p_contract_type text,
    IN p_signed_at date,
    IN p_start_date date,
    IN p_end_date date,
    IN p_position_id uuid,
    IN p_fte_numerator integer,
    IN p_fte_denominator integer,
    IN p_pay_basis text,
    IN p_base_amount numeric,
    IN p_zus_title_code text,
    IN p_zus_pension_code text,
    IN p_zus_disability_code text,
    IN p_insurances text[],
    IN p_cost_kind text,
    INOUT p_id uuid DEFAULT NULL
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_company_id uuid;
    v_number text := nullif(btrim(p_number), '');
    v_row employment.employments%ROWTYPE;
    v_terms employment.terms%ROWTYPE;
    v_errors jsonb;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    -- Blokada kartoteki: dwie równoległe umowy tej samej osoby nie ominą sprawdzenia nakładania i zbiegu.
    SELECT e.company_id INTO v_company_id FROM employees.employees e WHERE e.id = p_employee_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'employee not found' USING ERRCODE = 'U0002';
    END IF;
    PERFORM pg_advisory_xact_lock(hashtext('employment.number:' || v_company_id::text));
    IF v_number IS NULL THEN
        SELECT (coalesce(max(m.number::bigint), 0) + 1)::text INTO v_number
        FROM employment.employments m
        WHERE m.company_id = v_company_id AND m.number ~ '^\d{1,15}$';
    END IF;

    v_errors := employment.employment_validate(NULL, p_employee_id, v_company_id, v_number, p_kind,
        nullif(p_contract_type, ''), p_signed_at, p_start_date, p_end_date, NULL);
    IF NOT v_errors ? 'kind' THEN
        v_errors := v_errors || (employment.terms_validate(p_kind, p_start_date, p_position_id, NULL,
            p_fte_numerator, p_fte_denominator, p_pay_basis, p_base_amount, nullif(p_zus_title_code, ''),
            nullif(p_zus_pension_code, ''), nullif(p_zus_disability_code, ''), p_insurances, p_cost_kind)
            - 'validFrom');
    END IF;
    IF v_errors <> '{}' THEN
        PERFORM platform.fail_validation(v_errors);
    END IF;
    PERFORM employment.check_concurrency(p_employee_id, p_kind,
        employment.period(p_start_date, p_end_date, NULL), NULL);

    INSERT INTO employment.employments (employee_id, company_id, number, kind, contract_type, signed_at, start_date,
                                        end_date, created_by)
    VALUES (p_employee_id, v_company_id, v_number, p_kind, nullif(p_contract_type, ''), p_signed_at, p_start_date,
            p_end_date, platform.actor_id())
    RETURNING * INTO v_row;
    p_id := v_row.id;

    INSERT INTO employment.terms (employment_id, valid_from, position_id, fte_numerator, fte_denominator, pay_basis,
                                  base_amount, zus_title_code_id, zus_pension_code_id, zus_disability_code_id,
                                  insurances, cost_kind, created_by)
    VALUES (p_id, p_start_date, p_position_id, p_fte_numerator, p_fte_denominator, p_pay_basis, p_base_amount,
            employment.zus_code_id('title', nullif(p_zus_title_code, ''), p_start_date),
            employment.zus_code_id('pension', nullif(p_zus_pension_code, ''), p_start_date),
            employment.zus_code_id('disability', nullif(p_zus_disability_code, ''), p_start_date),
            employment.normalize_insurances(p_insurances), p_cost_kind, platform.actor_id())
    RETURNING * INTO v_terms;

    PERFORM platform.audit('employment', p_id::text, 'create', NULL, to_jsonb(v_row));
    PERFORM platform.audit('employment_terms', v_terms.id::text, 'create', NULL, to_jsonb(v_terms));
EXCEPTION
    WHEN unique_violation THEN
        PERFORM platform.fail_validation('{"number": ["duplicate"]}');
    WHEN exclusion_violation THEN
        PERFORM platform.fail_validation('{"startDate": ["overlap"]}');
END;
$$;

--changeset hr:employment_app.employment_update runOnChange:true splitStatements:false
--comment: Edycja umowy z wersją wiersza (R6). Rodzaju i kartoteki nie zmienia się; początek pierwszej wersji warunków
--comment: idzie za początkiem umowy. Przesunięcie początku albo końca w okres zamkniętej listy płac — U0003
--comment: payrollClosed, naliczonej — lista nieaktualna (009 R2, R25).
CREATE OR REPLACE PROCEDURE employment_app.employment_update(
    IN p_id uuid,
    IN p_row_version bigint,
    IN p_number text,
    IN p_contract_type text,
    IN p_signed_at date,
    IN p_start_date date,
    IN p_end_date date
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_before employment.employments%ROWTYPE;
    v_after employment.employments%ROWTYPE;
    v_first employment.terms%ROWTYPE;
    v_errors jsonb;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    SELECT * INTO v_before FROM employment.employments m WHERE m.id = p_id;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'employment not found' USING ERRCODE = 'U0002';
    END IF;
    PERFORM FROM employees.employees e WHERE e.id = v_before.employee_id FOR UPDATE;
    SELECT * INTO v_before FROM employment.employments m WHERE m.id = p_id FOR UPDATE;
    IF (SELECT m.xmin::text::bigint FROM employment.employments m WHERE m.id = p_id) <> p_row_version THEN
        RAISE EXCEPTION 'employment changed' USING ERRCODE = 'U0003';
    END IF;

    v_after := v_before;
    v_after.number := nullif(btrim(p_number), '');
    v_after.contract_type := nullif(p_contract_type, '');
    v_after.signed_at := p_signed_at;
    v_after.start_date := p_start_date;
    v_after.end_date := p_end_date;

    v_errors := employment.employment_validate(p_id, v_after.employee_id, v_after.company_id, v_after.number,
        v_after.kind, v_after.contract_type, v_after.signed_at, v_after.start_date, v_after.end_date,
        v_after.terminated_at);
    IF v_errors = '{}' THEN
        IF EXISTS (
            SELECT FROM employment.terms t
            WHERE t.employment_id = p_id
              AND t.valid_from > (SELECT min(f.valid_from) FROM employment.terms f WHERE f.employment_id = p_id)
              AND t.valid_from <= v_after.start_date
        ) THEN
            v_errors := v_errors || '{"startDate": ["termsOutsidePeriod"]}';
        END IF;
        IF EXISTS (
            SELECT FROM employment.terms t
            WHERE t.employment_id = p_id AND t.valid_from > coalesce(v_after.terminated_at, v_after.end_date)
        ) THEN
            v_errors := v_errors || '{"endDate": ["termsOutsidePeriod"]}';
        END IF;
    END IF;
    IF v_errors <> '{}' THEN
        PERFORM platform.fail_validation(v_errors);
    END IF;
    PERFORM employment.check_concurrency(v_after.employee_id, v_after.kind,
        employment.period(v_after.start_date, v_after.end_date, v_after.terminated_at), p_id);
    PERFORM payroll.guard_period_change(v_before.employee_id, p_id, v_before.start_date,
        coalesce(v_before.terminated_at, v_before.end_date), v_after.start_date,
        coalesce(v_after.terminated_at, v_after.end_date));

    UPDATE employment.employments m
    SET number = v_after.number, contract_type = v_after.contract_type, signed_at = v_after.signed_at,
        start_date = v_after.start_date, end_date = v_after.end_date
    WHERE m.id = p_id;

    SELECT * INTO v_first FROM employment.terms t WHERE t.employment_id = p_id ORDER BY t.valid_from LIMIT 1;
    IF v_first.valid_from <> v_after.start_date THEN
        UPDATE employment.terms t SET valid_from = v_after.start_date WHERE t.id = v_first.id;
    END IF;

    PERFORM platform.audit('employment', p_id::text, 'update', to_jsonb(v_before), to_jsonb(v_after));
EXCEPTION
    WHEN unique_violation THEN
        PERFORM platform.fail_validation('{"number": ["duplicate"]}');
    WHEN exclusion_violation THEN
        PERFORM platform.fail_validation('{"startDate": ["overlap"]}');
END;
$$;

--changeset hr:employment_app.employment_terminate runOnChange:true splitStatements:false
--comment: Rozwiązanie umowy datą albo jego cofnięcie pustą datą (R7). Zmiana końca w okresie zamkniętej listy płac —
--comment: U0003 payrollClosed, naliczonej — lista nieaktualna (009 R2, R25).
CREATE OR REPLACE PROCEDURE employment_app.employment_terminate(
    IN p_id uuid,
    IN p_row_version bigint,
    IN p_terminated_at date
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_before employment.employments%ROWTYPE;
    v_last_from date;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    SELECT * INTO v_before FROM employment.employments m WHERE m.id = p_id;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'employment not found' USING ERRCODE = 'U0002';
    END IF;
    PERFORM FROM employees.employees e WHERE e.id = v_before.employee_id FOR UPDATE;
    SELECT * INTO v_before FROM employment.employments m WHERE m.id = p_id FOR UPDATE;
    IF (SELECT m.xmin::text::bigint FROM employment.employments m WHERE m.id = p_id) <> p_row_version THEN
        RAISE EXCEPTION 'employment changed' USING ERRCODE = 'U0003';
    END IF;

    IF p_terminated_at IS NOT NULL THEN
        SELECT max(t.valid_from) INTO v_last_from FROM employment.terms t WHERE t.employment_id = p_id;
        IF p_terminated_at < v_before.start_date THEN
            PERFORM platform.fail_validation('{"terminatedAt": ["beforeStart"]}');
        ELSIF p_terminated_at > v_before.end_date THEN
            PERFORM platform.fail_validation('{"terminatedAt": ["afterEnd"]}');
        ELSIF p_terminated_at < v_last_from THEN
            PERFORM platform.fail_validation('{"terminatedAt": ["beforeCurrent"]}');
        END IF;
    END IF;
    -- Późniejsza data albo cofnięcie rozwiązania wydłuża okres — może wejść na inną umowę.
    PERFORM employment.check_concurrency(v_before.employee_id, v_before.kind,
        employment.period(v_before.start_date, v_before.end_date, p_terminated_at), p_id);

    PERFORM payroll.guard_period_change(v_before.employee_id, p_id, v_before.start_date,
        coalesce(v_before.terminated_at, v_before.end_date), v_before.start_date,
        coalesce(p_terminated_at, v_before.end_date));

    UPDATE employment.employments m SET terminated_at = p_terminated_at WHERE m.id = p_id;
    PERFORM platform.audit('employment', p_id::text, 'update',
        jsonb_build_object('terminated_at', v_before.terminated_at),
        jsonb_build_object('terminated_at', p_terminated_at));
EXCEPTION
    WHEN exclusion_violation THEN
        PERFORM platform.fail_validation('{"terminatedAt": ["overlap"]}');
END;
$$;

--changeset hr:employment_app.employment_delete runOnChange:true splitStatements:false
--comment: Usunięcie umowy wprowadzonej przez pomyłkę (R9). Umowa na zamkniętej liście płac — U0003 payrollClosed;
--comment: z list nie zamkniętych znika, naliczone stają się nieaktualne (009 R25).
CREATE OR REPLACE PROCEDURE employment_app.employment_delete(IN p_id uuid)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_row employment.employments%ROWTYPE;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    SELECT * INTO v_row FROM employment.employments m WHERE m.id = p_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'employment not found' USING ERRCODE = 'U0002';
    END IF;
    PERFORM payroll.guard_used('employment', p_id);

    DELETE FROM employment.employments m WHERE m.id = p_id;
    PERFORM platform.audit('employment', p_id::text, 'delete', to_jsonb(v_row), NULL);
END;
$$;

--changeset hr:employment_app.terms_add runOnChange:true splitStatements:false
--comment: Nowa wersja warunków od dnia (R14): poprzednia kończy się dzień wcześniej; ta sama data poprawia ostatnią.
--comment: Od dnia w okresie zamkniętej listy płac — U0003 payrollClosed, naliczonej — lista nieaktualna (009 [D]).
CREATE OR REPLACE PROCEDURE employment_app.terms_add(
    IN p_employment_id uuid,
    IN p_valid_from date,
    IN p_position_id uuid,
    IN p_fte_numerator integer,
    IN p_fte_denominator integer,
    IN p_pay_basis text,
    IN p_base_amount numeric,
    IN p_zus_title_code text,
    IN p_zus_pension_code text,
    IN p_zus_disability_code text,
    IN p_insurances text[],
    IN p_cost_kind text,
    INOUT p_id uuid DEFAULT NULL
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_employment employment.employments%ROWTYPE;
    v_last employment.terms%ROWTYPE;
    v_new employment.terms%ROWTYPE;
    v_errors jsonb;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    SELECT * INTO v_employment FROM employment.employments m WHERE m.id = p_employment_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'employment not found' USING ERRCODE = 'U0002';
    END IF;
    SELECT * INTO v_last FROM employment.terms t
    WHERE t.employment_id = p_employment_id ORDER BY t.valid_from DESC LIMIT 1;

    v_errors := employment.terms_validate(v_employment.kind, p_valid_from, p_position_id, v_last.position_id,
        p_fte_numerator, p_fte_denominator, p_pay_basis, p_base_amount, nullif(p_zus_title_code, ''),
        nullif(p_zus_pension_code, ''), nullif(p_zus_disability_code, ''), p_insurances, p_cost_kind);
    IF NOT v_errors ? 'validFrom' THEN
        IF NOT employment.period(v_employment.start_date, v_employment.end_date, v_employment.terminated_at)
            @> p_valid_from THEN
            v_errors := v_errors || '{"validFrom": ["outsidePeriod"]}';
        ELSIF p_valid_from < v_last.valid_from THEN
            v_errors := v_errors || '{"validFrom": ["beforeCurrent"]}';
        END IF;
    END IF;
    IF v_errors <> '{}' THEN
        PERFORM platform.fail_validation(v_errors);
    END IF;

    v_new.employment_id := p_employment_id;
    v_new.valid_from := p_valid_from;
    v_new.position_id := p_position_id;
    v_new.fte_numerator := p_fte_numerator;
    v_new.fte_denominator := p_fte_denominator;
    v_new.pay_basis := p_pay_basis;
    v_new.base_amount := p_base_amount;
    v_new.zus_title_code_id := employment.zus_code_id('title', nullif(p_zus_title_code, ''), p_valid_from);
    v_new.zus_pension_code_id := employment.zus_code_id('pension', nullif(p_zus_pension_code, ''), p_valid_from);
    v_new.zus_disability_code_id :=
        employment.zus_code_id('disability', nullif(p_zus_disability_code, ''), p_valid_from);
    v_new.insurances := employment.normalize_insurances(p_insurances);
    v_new.cost_kind := p_cost_kind;

    PERFORM payroll.guard(v_employment.employee_id, p_employment_id, p_valid_from, NULL);
    IF p_valid_from = v_last.valid_from THEN
        UPDATE employment.terms t
        SET position_id = v_new.position_id, fte_numerator = v_new.fte_numerator,
            fte_denominator = v_new.fte_denominator, pay_basis = v_new.pay_basis, base_amount = v_new.base_amount,
            zus_title_code_id = v_new.zus_title_code_id, zus_pension_code_id = v_new.zus_pension_code_id,
            zus_disability_code_id = v_new.zus_disability_code_id, insurances = v_new.insurances,
            cost_kind = v_new.cost_kind
        WHERE t.id = v_last.id
        RETURNING * INTO v_new;
        p_id := v_last.id;
        PERFORM platform.audit('employment_terms', p_id::text, 'update', to_jsonb(v_last), to_jsonb(v_new));
        RETURN;
    END IF;

    UPDATE employment.terms t SET valid_to = p_valid_from - 1 WHERE t.id = v_last.id;
    INSERT INTO employment.terms (employment_id, valid_from, position_id, fte_numerator, fte_denominator, pay_basis,
                                  base_amount, zus_title_code_id, zus_pension_code_id, zus_disability_code_id,
                                  insurances, cost_kind, created_by)
    VALUES (p_employment_id, p_valid_from, v_new.position_id, v_new.fte_numerator, v_new.fte_denominator,
            v_new.pay_basis, v_new.base_amount, v_new.zus_title_code_id, v_new.zus_pension_code_id,
            v_new.zus_disability_code_id, v_new.insurances, v_new.cost_kind, platform.actor_id())
    RETURNING * INTO v_new;
    p_id := v_new.id;
    PERFORM platform.audit('employment_terms', p_id::text, 'create', NULL, to_jsonb(v_new));
END;
$$;

--changeset hr:employment_app.terms_delete_last runOnChange:true splitStatements:false
--comment: Usunięcie ostatniej wersji warunków (R14); poprzednia znów jest otwarta. Jedynej wersji nie usuwa się.
--comment: Wersja w okresie zamkniętej listy płac — U0003 payrollClosed (009 [D]).
CREATE OR REPLACE PROCEDURE employment_app.terms_delete_last(IN p_employment_id uuid)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_last employment.terms%ROWTYPE;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    PERFORM FROM employment.employments m WHERE m.id = p_employment_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'employment not found' USING ERRCODE = 'U0002';
    END IF;
    IF (SELECT count(*) FROM employment.terms t WHERE t.employment_id = p_employment_id) < 2 THEN
        RAISE EXCEPTION 'the only terms version cannot be deleted' USING ERRCODE = 'U0003', HINT = 'onlyVersion';
    END IF;

    SELECT * INTO v_last FROM employment.terms t
    WHERE t.employment_id = p_employment_id ORDER BY t.valid_from DESC LIMIT 1;
    PERFORM payroll.guard(NULL, p_employment_id, v_last.valid_from, NULL);
    DELETE FROM employment.terms t WHERE t.id = v_last.id;
    UPDATE employment.terms t SET valid_to = NULL
    WHERE t.id = (SELECT p.id FROM employment.terms p
                  WHERE p.employment_id = p_employment_id ORDER BY p.valid_from DESC LIMIT 1);
    PERFORM platform.audit('employment_terms', v_last.id::text, 'delete', to_jsonb(v_last), NULL);
END;
$$;
