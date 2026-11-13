--liquibase formatted sql

--changeset hr:documents_app.company_tax_summary runOnChange:true splitStatements:false
--comment: Zestawienie spółki za rok (R12; Kadry, Administrator) — pomoc do PIT-4R(13) i PIT-8AR(14): dla każdego
--comment: miesiąca daty wypłaty zamkniętych list zaliczki (PIT) z umów o pracę, zleceń i dzieł oraz ryczałt
--comment: (PIT-RYCZ) ze zleceń i dzieł, liczba osób z zaliczką i z ryczałtem. Zawsze 12 wierszy.
DROP FUNCTION IF EXISTS documents_app.company_tax_summary(uuid, integer);
CREATE FUNCTION documents_app.company_tax_summary(p_company_id uuid, p_year integer)
    RETURNS TABLE (
        month integer,
        employment_advances numeric,
        mandate_advances numeric,
        specific_work_advances numeric,
        advances_total numeric,
        advance_people integer,
        mandate_flat_tax numeric,
        specific_work_flat_tax numeric,
        flat_tax_total numeric,
        flat_tax_people integer
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
    WITH t AS (
        SELECT extract(month FROM p.payment_date)::integer AS m, x.kind AS contract_kind, n.employee_id, l.code,
               l.amount
        FROM payroll.payrolls p
        JOIN payroll.entries n ON n.payroll_id = p.id
        JOIN employment.employments x ON x.id = n.employment_id
        JOIN payroll.lines l ON l.entry_id = n.id AND l.code IN ('PIT', 'PIT-RYCZ')
        WHERE p.company_id = p_company_id AND p.status = 'closed'
          AND p.payment_date >= make_date(p_year, 1, 1) AND p.payment_date < make_date(p_year + 1, 1, 1)
    )
    SELECT g.m,
           coalesce(sum(t.amount) FILTER (WHERE t.code = 'PIT' AND t.contract_kind = 'employment'), 0),
           coalesce(sum(t.amount) FILTER (WHERE t.code = 'PIT' AND t.contract_kind = 'mandate'), 0),
           coalesce(sum(t.amount) FILTER (WHERE t.code = 'PIT' AND t.contract_kind = 'specific_work'), 0),
           coalesce(sum(t.amount) FILTER (WHERE t.code = 'PIT'), 0),
           count(DISTINCT t.employee_id) FILTER (WHERE t.code = 'PIT' AND t.amount <> 0)::integer,
           coalesce(sum(t.amount) FILTER (WHERE t.code = 'PIT-RYCZ' AND t.contract_kind = 'mandate'), 0),
           coalesce(sum(t.amount) FILTER (WHERE t.code = 'PIT-RYCZ' AND t.contract_kind = 'specific_work'), 0),
           coalesce(sum(t.amount) FILTER (WHERE t.code = 'PIT-RYCZ'), 0),
           count(DISTINCT t.employee_id) FILTER (WHERE t.code = 'PIT-RYCZ' AND t.amount <> 0)::integer
    FROM generate_series(1, 12) g(m)
    LEFT JOIN t ON t.m = g.m
    GROUP BY g.m
    ORDER BY g.m;
END;
$$;

--changeset hr:documents_app.consent_document runOnChange:true splitStatements:false
--comment: Treść dokumentu zgody na potrącenie do podpisu (R11; KP art. 91): pracodawca (nazwa, NIP, adres), pracownik
--comment: (imię, nazwisko, numer, adres zamieszkania aktualny dziś), składnik potrącenia (kod, nazwa, kategoria),
--comment: kwota miesięczna, okres, data podpisania. Kadry i Administrator — każda; Pracownik — tylko swoja.
DROP FUNCTION IF EXISTS documents_app.consent_document(uuid);
CREATE FUNCTION documents_app.consent_document(p_consent_id uuid)
    RETURNS TABLE (
        id uuid,
        company_name text,
        company_nip text,
        company_address text,
        employee_id uuid,
        employee_number text,
        first_name text,
        last_name text,
        employee_address text,
        component_code text,
        component_name text,
        deduction_category text,
        monthly_amount numeric,
        valid_from date,
        valid_to date,
        signed_at date
    )
    LANGUAGE plpgsql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_employee_id uuid;
BEGIN
    SELECT d.employee_id INTO v_employee_id FROM pay_components.consents d WHERE d.id = p_consent_id;
    IF v_employee_id IS NULL THEN
        PERFORM documents.require_employee_access(NULL);
        RAISE EXCEPTION 'consent not found' USING ERRCODE = 'U0002';
    END IF;
    PERFORM documents.require_employee_access(v_employee_id);
    RETURN QUERY
    SELECT d.id, c.name, c.nip, c.street || ', ' || c.postal_code || ' ' || c.city, e.id, e.number, e.first_name,
           e.last_name,
           (SELECT a.street || ', ' || a.postal_code || ' ' || a.city FROM employees.addresses a
            WHERE a.employee_id = e.id AND a.kind = 'residence'
              AND current_date BETWEEN a.valid_from AND coalesce(a.valid_to, 'infinity')
            LIMIT 1),
           t.code, t.name, t.deduction_category, d.monthly_amount, d.valid_from, d.valid_to, d.signed_at
    FROM pay_components.consents d
    JOIN employees.employees e ON e.id = d.employee_id
    JOIN platform.companies c ON c.id = e.company_id
    JOIN pay_components.component_types t ON t.id = d.component_type_id
    WHERE d.id = p_consent_id;
END;
$$;
