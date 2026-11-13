--liquibase formatted sql

--changeset hr:20261113-18
--comment: Składniki płacowe, przypisania do umów, zgody na potrącenia i zajęcia wynagrodzenia (specyfikacja 006).
CREATE SCHEMA pay_components;
CREATE SCHEMA pay_components_app;

CREATE TABLE pay_components.component_types (
    id uuid PRIMARY KEY DEFAULT uuidv7(),
    code text NOT NULL,
    name text NOT NULL,
    kind text NOT NULL,
    calculation text NOT NULL,
    in_zus_base boolean NULL,
    in_health_base boolean NULL,
    taxable boolean NULL,
    in_sickness_base boolean NULL,
    deduction_category text NULL,
    is_system boolean NOT NULL DEFAULT false,
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid NULL,
    CONSTRAINT component_types_code CHECK (code ~ '^[A-Z0-9-]{1,20}$'),
    CONSTRAINT component_types_code_uq UNIQUE (code),
    CONSTRAINT component_types_name CHECK (char_length(name) BETWEEN 1 AND 200),
    CONSTRAINT component_types_kind CHECK (kind IN ('addition', 'deduction')),
    CONSTRAINT component_types_calculation CHECK (
        calculation IN ('fixed', 'percentOfBase', 'rateTimesHours', 'engine')
        AND (calculation <> 'engine' OR is_system)
        AND (kind = 'addition' OR calculation IN ('fixed', 'engine'))
    ),
    -- Cechy naliczenia ma tylko dodatek; kategorię tylko potrącenie (R3, R4).
    CONSTRAINT component_types_flags CHECK (
        (kind = 'addition' AND in_zus_base IS NOT NULL AND in_health_base IS NOT NULL AND taxable IS NOT NULL
            AND in_sickness_base IS NOT NULL AND deduction_category IS NULL)
        OR (kind = 'deduction' AND in_zus_base IS NULL AND in_health_base IS NULL AND taxable IS NULL
            AND in_sickness_base IS NULL AND deduction_category IS NOT NULL)
    ),
    -- Alimenty i inne egzekucje mają tylko składniki systemowe liczone z rejestru zajęć (R4).
    CONSTRAINT component_types_category CHECK (
        deduction_category IS NULL
        OR deduction_category IN ('advance', 'penalty', 'voluntaryEmployer', 'voluntaryOther')
        OR (is_system AND deduction_category IN ('alimony', 'enforcement'))
    )
);

CREATE TABLE pay_components.consents (
    id uuid PRIMARY KEY DEFAULT uuidv7(),
    employee_id uuid NOT NULL REFERENCES employees.employees (id) ON DELETE CASCADE,
    component_type_id uuid NOT NULL REFERENCES pay_components.component_types (id),
    monthly_amount numeric(12, 2) NOT NULL,
    valid_from date NOT NULL,
    valid_to date NULL,
    signed_at date NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid NULL,
    CONSTRAINT consents_amount CHECK (monthly_amount > 0),
    CONSTRAINT consents_period CHECK (valid_to IS NULL OR valid_to >= valid_from)
);
CREATE INDEX consents_employee_idx ON pay_components.consents (employee_id);

CREATE TABLE pay_components.employment_components (
    id uuid PRIMARY KEY DEFAULT uuidv7(),
    employment_id uuid NOT NULL REFERENCES employment.employments (id) ON DELETE CASCADE,
    component_type_id uuid NOT NULL REFERENCES pay_components.component_types (id),
    value numeric(12, 2) NOT NULL,
    valid_from date NOT NULL,
    valid_to date NULL,
    -- Bez kaskady: przy usunięciu kartoteki przypisania i zgody znikają w tej samej instrukcji (sprawdzenie na
    -- końcu), a usunięcie samej zgody w użyciu blokuje rutyna.
    consent_id uuid NULL REFERENCES pay_components.consents (id),
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid NULL,
    CONSTRAINT employment_components_value CHECK (value > 0),
    CONSTRAINT employment_components_period CHECK (valid_to IS NULL OR valid_to >= valid_from),
    CONSTRAINT employment_components_no_overlap EXCLUDE USING gist (
        employment_id WITH =, component_type_id WITH =, daterange(valid_from, valid_to, '[]') WITH &&
    )
);
CREATE INDEX employment_components_consent_idx ON pay_components.employment_components (consent_id);

CREATE TABLE pay_components.garnishments (
    id uuid PRIMARY KEY DEFAULT uuidv7(),
    employee_id uuid NOT NULL REFERENCES employees.employees (id) ON DELETE CASCADE,
    kind text NOT NULL,
    authority_kind text NOT NULL,
    authority_name text NOT NULL,
    case_number text NOT NULL,
    received_at date NOT NULL,
    monthly_amount numeric(12, 2) NULL,
    total_amount numeric(12, 2) NULL,
    status text NOT NULL DEFAULT 'active',
    status_date date NULL,
    note text NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid NULL,
    CONSTRAINT garnishments_kind CHECK (kind IN ('alimony', 'other')),
    CONSTRAINT garnishments_authority CHECK (
        authority_kind IN ('court', 'administrative', 'creditor') AND (authority_kind <> 'creditor' OR kind = 'alimony')
    ),
    CONSTRAINT garnishments_texts CHECK (
        char_length(authority_name) BETWEEN 1 AND 300 AND char_length(case_number) BETWEEN 1 AND 100
        AND (note IS NULL OR char_length(note) <= 2000)
    ),
    CONSTRAINT garnishments_amounts CHECK (
        (monthly_amount IS NULL OR monthly_amount > 0) AND (total_amount IS NULL OR total_amount > 0)
        AND (kind = 'alimony' AND (monthly_amount IS NOT NULL OR total_amount IS NOT NULL)
            OR kind = 'other' AND total_amount IS NOT NULL)
    ),
    CONSTRAINT garnishments_status CHECK (
        (status = 'active' AND status_date IS NULL)
        OR (status IN ('paidOff', 'discontinued') AND status_date >= received_at)
    )
);
CREATE UNIQUE INDEX garnishments_case_uq
    ON pay_components.garnishments (employee_id, lower(authority_name), lower(case_number));

-- Odczyt kwot na umowie i zajęć trafia do audytu odczytów (R12, R22).
ALTER TABLE platform.read_audit_entries DROP CONSTRAINT read_audit_entries_resource;
ALTER TABLE platform.read_audit_entries ADD CONSTRAINT read_audit_entries_resource
    CHECK (resource IN ('employee', 'employment_terms', 'employment_components', 'garnishment', 'payslip', 'tax_form',
                        'document'));
