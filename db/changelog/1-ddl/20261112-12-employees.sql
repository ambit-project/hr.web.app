--liquibase formatted sql

--changeset hr:20261112-12
--comment: Kartoteki pracowników (osobne w każdej spółce), adresy i rachunki z okresami. PESEL, dokument i rachunek
--comment: zaszyfrowane pgcrypto (ADR-0009); unikalność PESEL w spółce po HMAC.
CREATE SCHEMA employees;
CREATE SCHEMA employees_app;

CREATE TABLE employees.employees (
    id uuid PRIMARY KEY DEFAULT uuidv7(),
    company_id uuid NOT NULL REFERENCES platform.companies (id),
    number text NOT NULL,
    first_name text NOT NULL,
    second_name text NULL,
    last_name text NOT NULL,
    pesel_enc bytea NULL,
    pesel_hmac bytea NULL,
    identity_document_enc bytea NULL,
    birth_date date NOT NULL,
    tax_office_id uuid NULL REFERENCES dictionaries.tax_offices (id),
    email text NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid NULL,
    CONSTRAINT employees_number_length CHECK (char_length(number) BETWEEN 1 AND 20),
    CONSTRAINT employees_first_name_length CHECK (char_length(first_name) BETWEEN 1 AND 100),
    CONSTRAINT employees_last_name_length CHECK (char_length(last_name) BETWEEN 1 AND 100),
    CONSTRAINT employees_pesel_pair CHECK ((pesel_enc IS NULL) = (pesel_hmac IS NULL)),
    CONSTRAINT employees_company_number_uq UNIQUE (company_id, number),
    CONSTRAINT employees_company_pesel_uq UNIQUE (company_id, pesel_hmac)
);
CREATE INDEX employees_last_name_idx ON employees.employees (company_id, lower(last_name));

CREATE TABLE employees.addresses (
    id uuid PRIMARY KEY DEFAULT uuidv7(),
    employee_id uuid NOT NULL REFERENCES employees.employees (id) ON DELETE CASCADE,
    kind text NOT NULL,
    street text NOT NULL,
    postal_code text NOT NULL,
    city text NOT NULL,
    country text NOT NULL DEFAULT 'PL',
    valid_from date NOT NULL,
    valid_to date NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid NULL,
    CONSTRAINT addresses_kind CHECK (kind IN ('residence', 'correspondence')),
    CONSTRAINT addresses_postal_code CHECK (postal_code ~ '^\d{2}-\d{3}$'),
    CONSTRAINT addresses_period CHECK (valid_to IS NULL OR valid_to >= valid_from),
    CONSTRAINT addresses_no_overlap EXCLUDE USING gist (
        employee_id WITH =, kind WITH =, daterange(valid_from, valid_to, '[]') WITH &&
    )
);

CREATE TABLE employees.bank_accounts (
    id uuid PRIMARY KEY DEFAULT uuidv7(),
    employee_id uuid NOT NULL REFERENCES employees.employees (id) ON DELETE CASCADE,
    iban_enc bytea NOT NULL,
    iban_last4 text NOT NULL,
    valid_from date NOT NULL,
    valid_to date NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid NULL,
    CONSTRAINT bank_accounts_last4 CHECK (iban_last4 ~ '^\d{4}$'),
    CONSTRAINT bank_accounts_period CHECK (valid_to IS NULL OR valid_to >= valid_from),
    CONSTRAINT bank_accounts_no_overlap EXCLUDE USING gist (
        employee_id WITH =, daterange(valid_from, valid_to, '[]') WITH &&
    )
);

-- Konto ↔ kartoteka (profil Pracownika). company_id powielone, żeby baza pilnowała „jedno konto = najwyżej
-- jedna kartoteka w spółce”.
CREATE TABLE platform.user_employee_links (
    id uuid PRIMARY KEY DEFAULT uuidv7(),
    user_id uuid NOT NULL REFERENCES platform.users (id),
    employee_id uuid NOT NULL REFERENCES employees.employees (id) ON DELETE CASCADE,
    company_id uuid NOT NULL REFERENCES platform.companies (id),
    linked_at timestamptz NOT NULL DEFAULT now(),
    linked_by uuid NULL,
    CONSTRAINT user_employee_links_employee_uq UNIQUE (employee_id),
    CONSTRAINT user_employee_links_user_company_uq UNIQUE (user_id, company_id)
);
