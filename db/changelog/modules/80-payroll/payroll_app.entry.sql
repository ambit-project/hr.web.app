--liquibase formatted sql

--changeset hr:payroll_app.entry_list runOnChange:true splitStatements:false
--comment: Wypłaty listy (R21–R24; Kadry, Administrator): osoba, brutto, składki pracownika, zdrowotna, PIT, PPK
--comment: pracownika, netto, potrącenia, do wypłaty, koszt pracodawcy, ostrzeżenia. Kwoty osób — audyt odczytu
--comment: (payslip), więc VOLATILE.
DROP FUNCTION IF EXISTS payroll_app.entry_list(uuid);
CREATE FUNCTION payroll_app.entry_list(p_payroll_id uuid)
    RETURNS TABLE (
        id uuid,
        employment_id uuid,
        employment_number text,
        employee_id uuid,
        employee_number text,
        acronym text,
        first_name text,
        last_name text,
        gross numeric,
        employee_contributions numeric,
        health numeric,
        pit numeric,
        ppk_employee numeric,
        net numeric,
        deductions numeric,
        payout numeric,
        employer_cost numeric,
        warnings jsonb
    )
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    IF NOT EXISTS (SELECT FROM payroll.payrolls p WHERE p.id = p_payroll_id) THEN
        RAISE EXCEPTION 'payroll not found' USING ERRCODE = 'U0002';
    END IF;
    PERFORM platform.read_audit(e.employee_id, 'payslip', e.id::text)
    FROM payroll.entries e WHERE e.payroll_id = p_payroll_id;

    RETURN QUERY
    SELECT n.id, n.employment_id, x.number, e.id, e.number, e.acronym, e.first_name, e.last_name, n.gross,
           coalesce(sum(l.amount) FILTER (WHERE l.code IN ('ZUS-E-P', 'ZUS-R-P', 'ZUS-C-P')), 0),
           coalesce(sum(l.amount) FILTER (WHERE l.code = 'ZDROW'), 0),
           coalesce(sum(l.amount) FILTER (WHERE l.code IN ('PIT', 'PIT-RYCZ')), 0),
           coalesce(sum(l.amount) FILTER (WHERE l.code IN ('PPK-P', 'PPK-PD')), 0),
           n.net,
           coalesce(sum(l.amount) FILTER (WHERE l.side = 'deduction'), 0),
           n.payout,
           coalesce(sum(l.amount) FILTER (WHERE l.side = 'employer'), 0),
           n.warnings
    FROM payroll.entries n
    JOIN employment.employments x ON x.id = n.employment_id
    JOIN employees.employees e ON e.id = n.employee_id
    LEFT JOIN payroll.lines l ON l.entry_id = n.id
    WHERE n.payroll_id = p_payroll_id
    GROUP BY n.id, x.number, e.id
    ORDER BY e.last_name, e.first_name, x.number;
END;
$$;

--changeset hr:payroll_app.entry_get runOnChange:true splitStatements:false
--comment: Wypłata z listą i godzinami (R24; Kadry, Administrator). Audyt odczytu (payslip).
DROP FUNCTION IF EXISTS payroll_app.entry_get(uuid);
CREATE FUNCTION payroll_app.entry_get(p_entry_id uuid)
    RETURNS TABLE (
        id uuid,
        payroll_id uuid,
        payroll_number text,
        payroll_status text,
        employment_id uuid,
        employment_number text,
        employee_id uuid,
        employee_number text,
        first_name text,
        last_name text,
        terms_id uuid,
        gross numeric,
        net numeric,
        payout numeric,
        norm_hours numeric,
        worked_hours numeric,
        overtime_hours numeric,
        warnings jsonb
    )
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_employee_id uuid;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    SELECT n.employee_id INTO v_employee_id FROM payroll.entries n WHERE n.id = p_entry_id;
    IF v_employee_id IS NULL THEN
        RAISE EXCEPTION 'entry not found' USING ERRCODE = 'U0002';
    END IF;
    PERFORM platform.read_audit(v_employee_id, 'payslip', p_entry_id::text);
    RETURN QUERY
    SELECT n.id, p.id, p.number, p.status, n.employment_id, x.number, e.id, e.number, e.first_name, e.last_name,
           n.terms_id, n.gross, n.net, n.payout, n.norm_hours, n.worked_hours, n.overtime_hours, n.warnings
    FROM payroll.entries n
    JOIN payroll.payrolls p ON p.id = n.payroll_id
    JOIN employment.employments x ON x.id = n.employment_id
    JOIN employees.employees e ON e.id = n.employee_id
    WHERE n.id = p_entry_id;
END;
$$;

--changeset hr:payroll_app.entry_lines runOnChange:true splitStatements:false
--comment: Pozycje wypłaty ze śladem (R24): kod, strona, podstawa, stawka, kwota, składnik, cechy przychodu, ślad.
--comment: Audyt odczytu (payslip).
DROP FUNCTION IF EXISTS payroll_app.entry_lines(uuid);
CREATE FUNCTION payroll_app.entry_lines(p_entry_id uuid)
    RETURNS TABLE (
        seq integer,
        code text,
        side text,
        base numeric,
        rate numeric,
        amount numeric,
        component_type_id uuid,
        component_name text,
        in_zus_base boolean,
        in_health_base boolean,
        taxable boolean,
        in_sickness_base boolean,
        trace jsonb
    )
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_employee_id uuid;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    SELECT n.employee_id INTO v_employee_id FROM payroll.entries n WHERE n.id = p_entry_id;
    IF v_employee_id IS NULL THEN
        RAISE EXCEPTION 'entry not found' USING ERRCODE = 'U0002';
    END IF;
    PERFORM platform.read_audit(v_employee_id, 'payslip', p_entry_id::text);
    RETURN QUERY
    SELECT l.seq, l.code, l.side, l.base, l.rate, l.amount, l.component_type_id, t.name, l.in_zus_base,
           l.in_health_base, l.taxable, l.in_sickness_base, l.trace
    FROM payroll.lines l
    LEFT JOIN pay_components.component_types t ON t.id = l.component_type_id
    WHERE l.entry_id = p_entry_id
    ORDER BY l.seq;
END;
$$;

--changeset hr:payroll_app.year_totals runOnChange:true splitStatements:false
--comment: Sumy roczne kartoteki z zamkniętych list (R25): podstawa emerytalno-rentowa (po okresie), podstawa PIT
--comment: umów o pracę i wszystkich umów, przychód zwolniony, koszty 50% i pracownicze (po dacie wypłaty). Audyt
--comment: odczytu (payslip).
DROP FUNCTION IF EXISTS payroll_app.year_totals(uuid, integer);
CREATE FUNCTION payroll_app.year_totals(p_employee_id uuid, p_year integer)
    RETURNS TABLE (
        pension_base numeric,
        tax_base_employment numeric,
        tax_base_all numeric,
        exempt_income numeric,
        author_costs numeric,
        employee_costs numeric
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
    PERFORM platform.read_audit(p_employee_id, 'payslip', 'year:' || p_year);
    RETURN QUERY
    SELECT t.pension_base, t.tax_base_employment, t.tax_base_all, t.exempt_income, t.author_costs, t.employee_costs
    FROM payroll.year_totals(p_employee_id, p_year) t;
END;
$$;
