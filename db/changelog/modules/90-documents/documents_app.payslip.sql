--liquibase formatted sql

--changeset hr:documents_app.payslip runOnChange:true splitStatements:false
--comment: Pasek wypłaty z zamkniętej listy (R1, R3): spółka, osoba, umowa, lista (okres, data wypłaty), brutto, netto,
--comment: do wypłaty, podstawy (ZUS, zdrowotna, PIT, koszty) i pozycje (lines: seq, code, side, name, base, rate,
--comment: amount). Kadry i Administrator — każda wypłata; Pracownik — tylko swoja. Lista niezamknięta → U0003
--comment: notClosed. Odczyt kogoś innego niż pracownik — audyt odczytu (payslip).
DROP FUNCTION IF EXISTS documents_app.payslip(uuid);
CREATE FUNCTION documents_app.payslip(p_entry_id uuid)
    RETURNS TABLE (
        id uuid,
        payroll_id uuid,
        payroll_number text,
        payroll_kind text,
        period_from date,
        period_to date,
        payment_date date,
        company_name text,
        company_nip text,
        company_address text,
        employee_id uuid,
        employee_number text,
        first_name text,
        last_name text,
        employment_number text,
        contract_kind text,
        gross numeric,
        net numeric,
        payout numeric,
        zus_base numeric,
        health_base numeric,
        tax_base numeric,
        costs numeric,
        lines jsonb
    )
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_employee_id uuid;
    v_status text;
BEGIN
    SELECT n.employee_id, p.status INTO v_employee_id, v_status
    FROM payroll.entries n JOIN payroll.payrolls p ON p.id = n.payroll_id
    WHERE n.id = p_entry_id;
    IF v_employee_id IS NULL THEN
        -- Pracownik bez roli Kadr nie dowie się, czy cudza wypłata istnieje.
        PERFORM documents.require_employee_access(NULL);
        RAISE EXCEPTION 'entry not found' USING ERRCODE = 'U0002';
    END IF;
    PERFORM documents.require_employee_access(v_employee_id);
    IF v_status <> 'closed' THEN
        RAISE EXCEPTION 'payroll is not closed' USING ERRCODE = 'U0003', HINT = 'notClosed';
    END IF;
    PERFORM platform.read_audit(v_employee_id, 'payslip', p_entry_id::text);

    RETURN QUERY
    SELECT n.id, p.id, p.number, p.kind, p.period_from, p.period_to, p.payment_date, c.name, c.nip,
           c.street || ', ' || c.postal_code || ' ' || c.city,
           e.id, e.number, e.first_name, e.last_name, x.number, x.kind, n.gross, n.net, n.payout,
           (SELECT coalesce(sum(l.amount), 0) FROM payroll.lines l WHERE l.entry_id = n.id AND l.code = 'PODST-ZUS'),
           (SELECT coalesce(sum(l.base), 0) FROM payroll.lines l WHERE l.entry_id = n.id AND l.code = 'ZDROW'),
           (SELECT coalesce(sum(l.amount), 0) FROM payroll.lines l WHERE l.entry_id = n.id AND l.code = 'PODST-PIT'),
           (SELECT coalesce(sum(l.amount), 0) FROM payroll.lines l
            WHERE l.entry_id = n.id AND l.code IN ('KUP', 'KUP50')),
           (SELECT coalesce(jsonb_agg(jsonb_build_object(
                       'seq', l.seq, 'code', l.code, 'side', l.side, 'name', t.name, 'base', l.base,
                       'rate', l.rate, 'amount', l.amount) ORDER BY l.seq), '[]')
            FROM payroll.lines l
            LEFT JOIN pay_components.component_types t ON t.id = l.component_type_id
            WHERE l.entry_id = n.id)
    FROM payroll.entries n
    JOIN payroll.payrolls p ON p.id = n.payroll_id
    JOIN platform.companies c ON c.id = p.company_id
    JOIN employees.employees e ON e.id = n.employee_id
    JOIN employment.employments x ON x.id = n.employment_id
    WHERE n.id = p_entry_id;
END;
$$;

--changeset hr:documents_app.my_payslips runOnChange:true splitStatements:false
--comment: Paski kartoteki według miesięcy (R3): wypłaty z zamkniętych list, od najnowszej daty wypłaty. Kadry
--comment: i Administrator — każda kartoteka (zakładka Dokumenty); Pracownik — tylko swoja. Audyt odczytu (payslip).
DROP FUNCTION IF EXISTS documents_app.my_payslips(uuid);
CREATE FUNCTION documents_app.my_payslips(p_employee_id uuid)
    RETURNS TABLE (
        id uuid,
        payroll_number text,
        payroll_kind text,
        period_from date,
        payment_date date,
        employment_number text,
        contract_kind text,
        gross numeric,
        net numeric,
        payout numeric
    )
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    PERFORM documents.require_employee_access(p_employee_id);
    PERFORM platform.read_audit(p_employee_id, 'payslip', 'list');
    RETURN QUERY
    SELECT n.id, p.number, p.kind, p.period_from, p.payment_date, x.number, x.kind, n.gross, n.net, n.payout
    FROM payroll.entries n
    JOIN payroll.payrolls p ON p.id = n.payroll_id AND p.status = 'closed'
    JOIN employment.employments x ON x.id = n.employment_id
    WHERE n.employee_id = p_employee_id
    ORDER BY p.payment_date DESC, p.period_from DESC, p.number, x.number;
END;
$$;

--changeset hr:documents_app.wage_card runOnChange:true splitStatements:false
--comment: Karta wynagrodzeń kartoteki za rok (R4; rozporządzenie o dokumentacji pracowniczej § 6 pkt 3): wiersz na
--comment: każdą wypłatę zamkniętej listy z datą wypłaty w roku — przychód, podstawa ZUS, składki pracownika
--comment: i pracodawcy (z FP i FGŚP), zdrowotna, koszty, zaliczka PIT (z ryczałtem), PPK pracownika i pracodawcy,
--comment: potrącenia, netto, do wypłaty. Kadry i Administrator. Audyt odczytu (payslip).
DROP FUNCTION IF EXISTS documents_app.wage_card(uuid, integer);
CREATE FUNCTION documents_app.wage_card(p_employee_id uuid, p_year integer)
    RETURNS TABLE (
        entry_id uuid,
        payroll_number text,
        payroll_kind text,
        period_from date,
        payment_date date,
        employment_number text,
        contract_kind text,
        gross numeric,
        zus_base numeric,
        employee_contributions numeric,
        employer_contributions numeric,
        health numeric,
        costs numeric,
        pit numeric,
        ppk_employee numeric,
        ppk_employer numeric,
        deductions numeric,
        net numeric,
        payout numeric
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
    PERFORM platform.read_audit(p_employee_id, 'payslip', 'wageCard:' || p_year);
    RETURN QUERY
    SELECT n.id, p.number, p.kind, p.period_from, p.payment_date, x.number, x.kind, n.gross,
           coalesce(sum(l.amount) FILTER (WHERE l.code = 'PODST-ZUS'), 0),
           coalesce(sum(l.amount) FILTER (WHERE l.code IN ('ZUS-E-P', 'ZUS-R-P', 'ZUS-C-P')), 0),
           coalesce(sum(l.amount) FILTER (WHERE l.code IN ('ZUS-E-PR', 'ZUS-R-PR', 'ZUS-W-PR', 'FP', 'FGSP')), 0),
           coalesce(sum(l.amount) FILTER (WHERE l.code = 'ZDROW'), 0),
           coalesce(sum(l.amount) FILTER (WHERE l.code IN ('KUP', 'KUP50')), 0),
           coalesce(sum(l.amount) FILTER (WHERE l.code IN ('PIT', 'PIT-RYCZ')), 0),
           coalesce(sum(l.amount) FILTER (WHERE l.code IN ('PPK-P', 'PPK-PD')), 0),
           coalesce(sum(l.amount) FILTER (WHERE l.code IN ('PPK-PR', 'PPK-PRD')), 0),
           coalesce(sum(l.amount) FILTER (WHERE l.side = 'deduction'), 0),
           n.net, n.payout
    FROM payroll.entries n
    JOIN payroll.payrolls p ON p.id = n.payroll_id AND p.status = 'closed'
    JOIN employment.employments x ON x.id = n.employment_id
    LEFT JOIN payroll.lines l ON l.entry_id = n.id
    WHERE n.employee_id = p_employee_id
      AND p.payment_date >= make_date(p_year, 1, 1) AND p.payment_date < make_date(p_year + 1, 1, 1)
    GROUP BY n.id, p.id, x.id
    ORDER BY p.payment_date, p.period_from, p.number, x.number;
END;
$$;
