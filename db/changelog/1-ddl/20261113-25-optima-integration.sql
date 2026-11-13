--liquibase formatted sql

--changeset hr:20261113-25
--comment: Integracja z Comarch ERP Optima przez agenta (specyfikacja 011, ADR-0010): klucze agenta, kolejka operacji,
--comment: bazy firm Optimy i ich powiązanie ze spółkami, rodzaje płac, listy płac pobrane z Optimy (tylko do odczytu).
CREATE SCHEMA optima;
CREATE SCHEMA optima_app;

-- Klucz agenta: w bazie tylko skrót SHA-256 tokenu (token ma 256 bitów losowości, sól niepotrzebna).
CREATE TABLE optima.agent_keys (
    id uuid PRIMARY KEY DEFAULT uuidv7(),
    name text NOT NULL,
    key_hash bytea NOT NULL,
    key_prefix text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid NULL,
    revoked_at timestamptz NULL,
    revoked_by uuid NULL,
    last_seen_at timestamptz NULL,
    last_agent_version text NULL,
    CONSTRAINT agent_keys_name CHECK (char_length(name) BETWEEN 1 AND 100),
    CONSTRAINT agent_keys_hash CHECK (octet_length(key_hash) = 32),
    CONSTRAINT agent_keys_hash_uq UNIQUE (key_hash)
);

-- Bazy firm, które agent zgłosił (nazwa bazy w Optimie i NIP z jej listy firm).
CREATE TABLE optima.databases (
    database_name text PRIMARY KEY,
    tax_number text NULL,
    last_seen_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT databases_name CHECK (database_name ~ '^[^\[\]\x00-\x1F]{1,128}$')
);

-- Spółka hr.web.app ↔ baza firmy Optimy (1:1).
CREATE TABLE optima.company_links (
    company_id uuid PRIMARY KEY REFERENCES platform.companies (id) ON DELETE CASCADE,
    database_name text NOT NULL,
    linked_at timestamptz NOT NULL DEFAULT now(),
    linked_by uuid NULL,
    CONSTRAINT company_links_name CHECK (database_name ~ '^[^\[\]\x00-\x1F]{1,128}$'),
    CONSTRAINT company_links_database_uq UNIQUE (database_name)
);

CREATE TABLE optima.operations (
    id uuid PRIMARY KEY DEFAULT uuidv7(),
    kind text NOT NULL,
    company_id uuid NULL REFERENCES platform.companies (id) ON DELETE CASCADE,
    database_name text NULL,
    year integer NULL,
    month integer NULL,
    employee_ids uuid[] NULL,
    -- Pozycje pominięte przy zleceniu (np. składnik bez powiązania z rodzajem płacy Optimy): [{id, reason}].
    skipped jsonb NOT NULL DEFAULT '[]',
    state text NOT NULL DEFAULT 'pending',
    attempt integer NOT NULL DEFAULT 1,
    claimed_at timestamptz NULL,
    claimed_by uuid NULL REFERENCES optima.agent_keys (id),
    finished_at timestamptz NULL,
    succeeded boolean NULL,
    error_code text NULL,
    log jsonb NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid NULL,
    CONSTRAINT operations_kind CHECK (
        kind IN ('ReadPayrollComponents', 'AddEmployees', 'ExportDeductions', 'ExportAllowances', 'ReadPayrolls')
    ),
    CONSTRAINT operations_state CHECK (state IN ('pending', 'claimed', 'succeeded', 'failed')),
    CONSTRAINT operations_attempt CHECK (attempt >= 1),
    CONSTRAINT operations_company CHECK (
        (kind = 'ReadPayrollComponents' AND company_id IS NULL AND database_name IS NULL)
        OR (kind <> 'ReadPayrollComponents' AND company_id IS NOT NULL AND database_name IS NOT NULL)
    ),
    CONSTRAINT operations_period CHECK (
        (kind IN ('ExportDeductions', 'ExportAllowances', 'ReadPayrolls')
            AND year BETWEEN 2000 AND 2100 AND month BETWEEN 1 AND 12)
        OR (kind NOT IN ('ExportDeductions', 'ExportAllowances', 'ReadPayrolls') AND year IS NULL AND month IS NULL)
    ),
    CONSTRAINT operations_employees CHECK ((kind = 'AddEmployees') = (employee_ids IS NOT NULL))
);
-- Agent pyta o pracę co 500 ms: pusty claim to jeden odczyt tego indeksu.
CREATE INDEX operations_pending_idx ON optima.operations (created_at, id) WHERE state = 'pending';
CREATE INDEX operations_created_idx ON optima.operations (created_at DESC, id);

-- Rodzaje płac (typy wypłat) z bazy firmy Optimy; id ma sens tylko w tej bazie.
CREATE TABLE optima.pay_types (
    database_name text NOT NULL,
    optima_id integer NOT NULL,
    name text NOT NULL,
    short_name text NULL,
    is_deduction boolean NOT NULL,
    source_kind integer NULL,
    standard_id integer NULL,
    default_amount numeric(15, 2) NULL,
    is_pkzp boolean NOT NULL,
    is_seizure boolean NOT NULL,
    is_active boolean NOT NULL,
    is_withdrawn boolean NOT NULL,
    imported_at timestamptz NOT NULL DEFAULT now(),
    -- Rodzaj, którego nie było w ostatnim odczycie bazy (usunięty w Optimie); nie kasujemy, bo może być powiązany.
    missing_since timestamptz NULL,
    component_type_id uuid NULL REFERENCES pay_components.component_types (id),
    PRIMARY KEY (database_name, optima_id)
);
CREATE UNIQUE INDEX pay_types_component_uq ON optima.pay_types (database_name, component_type_id)
    WHERE component_type_id IS NOT NULL;

-- Listy płac pobrane z Optimy: kopia tylko do odczytu, niezależna od przyszłego modułu list płac (009).
CREATE TABLE optima.payroll_lists (
    id uuid PRIMARY KEY DEFAULT uuidv7(),
    database_name text NOT NULL,
    optima_id integer NOT NULL,
    year integer NOT NULL,
    month integer NOT NULL,
    number text NOT NULL,
    name text NULL,
    period_from date NULL,
    period_to date NULL,
    document_date date NULL,
    is_closed boolean NOT NULL,
    imported_at timestamptz NOT NULL DEFAULT now(),
    operation_id uuid NULL REFERENCES optima.operations (id) ON DELETE SET NULL,
    CONSTRAINT payroll_lists_uq UNIQUE (database_name, optima_id)
);
CREATE INDEX payroll_lists_period_idx ON optima.payroll_lists (database_name, year, month);

CREATE TABLE optima.payouts (
    id uuid PRIMARY KEY DEFAULT uuidv7(),
    payroll_list_id uuid NOT NULL REFERENCES optima.payroll_lists (id) ON DELETE CASCADE,
    optima_id integer NOT NULL,
    optima_employee_id integer NULL,
    employee_id uuid NULL REFERENCES employees.employees (id) ON DELETE SET NULL,
    employee_code text NULL,
    gross numeric(15, 2) NULL,
    net numeric(15, 2) NULL,
    is_closed boolean NOT NULL,
    CONSTRAINT payouts_uq UNIQUE (payroll_list_id, optima_id)
);
CREATE INDEX payouts_employee_idx ON optima.payouts (employee_id);

CREATE TABLE optima.payout_elements (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    payout_id uuid NOT NULL REFERENCES optima.payouts (id) ON DELETE CASCADE,
    optima_id integer NULL,
    component_optima_id integer NULL,
    name text NOT NULL,
    amount numeric(15, 2) NOT NULL
);
CREATE INDEX payout_elements_payout_idx ON optima.payout_elements (payout_id);

-- Odczyt list płac z Optimy (kwoty pracownika) i eksport kartotek do Optimy trafiają do audytu odczytów.
ALTER TABLE platform.read_audit_entries DROP CONSTRAINT read_audit_entries_resource;
ALTER TABLE platform.read_audit_entries ADD CONSTRAINT read_audit_entries_resource
    CHECK (resource IN ('employee', 'employment_terms', 'employment_components', 'garnishment', 'payslip', 'tax_form',
                        'document', 'optima_export', 'optima_payroll'));
