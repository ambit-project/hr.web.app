--liquibase formatted sql

--changeset hr:20261112-14
--comment: Umowy (o pracę, zlecenie, dzieło) i wersje ich warunków z okresem obowiązywania (specyfikacja 004).
CREATE SCHEMA employment;
CREATE SCHEMA employment_app;

CREATE TABLE employment.employments (
    id uuid PRIMARY KEY DEFAULT uuidv7(),
    employee_id uuid NOT NULL REFERENCES employees.employees (id) ON DELETE CASCADE,
    company_id uuid NOT NULL REFERENCES platform.companies (id),
    number text NOT NULL,
    kind text NOT NULL,
    contract_type text NULL,
    signed_at date NOT NULL,
    start_date date NOT NULL,
    end_date date NULL,
    terminated_at date NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid NULL,
    CONSTRAINT employments_kind CHECK (kind IN ('employment', 'mandate', 'specific_work')),
    CONSTRAINT employments_contract_type CHECK (
        (kind = 'employment' AND contract_type IN ('trial', 'fixed_term', 'indefinite'))
        OR (kind <> 'employment' AND contract_type IS NULL)
    ),
    CONSTRAINT employments_end CHECK (
        (contract_type = 'indefinite' AND end_date IS NULL)
        OR (coalesce(contract_type, '') <> 'indefinite' AND end_date IS NOT NULL)
    ),
    CONSTRAINT employments_number_length CHECK (char_length(number) BETWEEN 1 AND 40),
    CONSTRAINT employments_dates CHECK (
        signed_at <= start_date
        AND (end_date IS NULL OR end_date >= start_date)
        AND (terminated_at IS NULL OR (terminated_at >= start_date AND (end_date IS NULL OR terminated_at <= end_date)))
    ),
    CONSTRAINT employments_company_number_uq UNIQUE (company_id, number),
    -- Dwie umowy o pracę jednej kartoteki nie nakładają się (R4); zbieg z innymi rodzajami sprawdza rutyna (R5).
    CONSTRAINT employments_employment_no_overlap EXCLUDE USING gist (
        employee_id WITH =, daterange(start_date, coalesce(terminated_at, end_date), '[]') WITH &&
    ) WHERE (kind = 'employment')
);
CREATE INDEX employments_employee_idx ON employment.employments (employee_id);

CREATE TABLE employment.terms (
    id uuid PRIMARY KEY DEFAULT uuidv7(),
    employment_id uuid NOT NULL REFERENCES employment.employments (id) ON DELETE CASCADE,
    valid_from date NOT NULL,
    valid_to date NULL,
    position_id uuid NULL REFERENCES dictionaries.positions (id),
    fte_numerator integer NULL,
    fte_denominator integer NULL,
    pay_basis text NOT NULL,
    base_amount numeric(12, 2) NOT NULL,
    zus_title_code_id uuid NULL REFERENCES dictionaries.zus_codes (id),
    zus_pension_code_id uuid NULL REFERENCES dictionaries.zus_codes (id),
    zus_disability_code_id uuid NULL REFERENCES dictionaries.zus_codes (id),
    insurances text[] NOT NULL DEFAULT '{}',
    cost_kind text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid NULL,
    CONSTRAINT terms_pay_basis CHECK (pay_basis IN ('monthly', 'hourly', 'lumpSum')),
    CONSTRAINT terms_cost_kind CHECK (cost_kind IN ('standard', 'increased', 'flat20', 'author50')),
    CONSTRAINT terms_base_amount CHECK (base_amount > 0),
    CONSTRAINT terms_fte CHECK (
        (fte_numerator IS NULL AND fte_denominator IS NULL)
        OR (fte_numerator > 0 AND fte_denominator BETWEEN 1 AND 1000 AND fte_numerator <= fte_denominator)
    ),
    CONSTRAINT terms_insurances CHECK (
        insurances <@ ARRAY['pension', 'disability', 'sickness', 'accident', 'health', 'fp', 'fgsp']
    ),
    CONSTRAINT terms_period CHECK (valid_to IS NULL OR valid_to >= valid_from),
    CONSTRAINT terms_no_overlap EXCLUDE USING gist (
        employment_id WITH =, daterange(valid_from, valid_to, '[]') WITH &&
    )
);

-- Odczyt warunków umowy (z wynagrodzeniem) trafia do audytu odczytów (R19).
ALTER TABLE platform.read_audit_entries DROP CONSTRAINT read_audit_entries_resource;
ALTER TABLE platform.read_audit_entries ADD CONSTRAINT read_audit_entries_resource
    CHECK (resource IN ('employee', 'employment_terms', 'payslip', 'tax_form', 'document'));
