--liquibase formatted sql

--changeset hr:20261113-32
--comment: Listy płac (specyfikacja 009): ustawienia płacowe spółki z okresem, listy (zwykłe i korygujące), skład listy,
--comment: wypłaty umów i pozycje naliczenia ze śladem. Sumy roczne bez tabeli — liczone z pozycji zamkniętych list.
CREATE SCHEMA payroll;
CREATE SCHEMA payroll_app;

-- Stopa wypadkowa płatnika i wypłata zasiłków przez płatnika (R6, R13; ustawa zasiłkowa art. 61 — powyżej 20
-- ubezpieczonych, ustawienie Kadr). Brak okresu = stopa domyślna z parametru zus.wypadkowa.domyslna, bez zasiłków.
CREATE TABLE payroll.company_settings (
    id uuid PRIMARY KEY DEFAULT uuidv7(),
    company_id uuid NOT NULL REFERENCES platform.companies (id) ON DELETE CASCADE,
    valid_from date NOT NULL,
    valid_to date NULL,
    accident_rate numeric(6, 4) NOT NULL,
    pays_sickness_allowance boolean NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid NULL,
    CONSTRAINT company_settings_valid_from CHECK (extract(day FROM valid_from) = 1),
    CONSTRAINT company_settings_period CHECK (valid_to IS NULL OR valid_to >= valid_from),
    CONSTRAINT company_settings_rate CHECK (accident_rate > 0 AND accident_rate < 0.2),
    CONSTRAINT company_settings_no_overlap EXCLUDE USING gist (
        company_id WITH =, daterange(valid_from, valid_to, '[]') WITH &&
    )
);

-- Lista płac (R1, R2, R4). Okres = miesiąc kalendarzowy; korekta ma okres listy korygowanej.
CREATE TABLE payroll.payrolls (
    id uuid PRIMARY KEY DEFAULT uuidv7(),
    company_id uuid NOT NULL REFERENCES platform.companies (id),
    number text NOT NULL,
    kind text NOT NULL,
    period_from date NOT NULL,
    period_to date NOT NULL,
    payment_date date NOT NULL,
    corrects_payroll_id uuid NULL REFERENCES payroll.payrolls (id),
    status text NOT NULL DEFAULT 'open',
    is_stale boolean NOT NULL DEFAULT false,
    calculated_at timestamptz NULL,
    closed_at timestamptz NULL,
    closed_by uuid NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid NULL,
    CONSTRAINT payrolls_kind CHECK (kind IN ('employment', 'mandate', 'specificWork', 'correction')),
    CONSTRAINT payrolls_status CHECK (status IN ('open', 'calculated', 'closed')),
    CONSTRAINT payrolls_period CHECK (
        extract(day FROM period_from) = 1 AND period_to = (period_from + interval '1 month - 1 day')::date
    ),
    CONSTRAINT payrolls_corrects CHECK ((kind = 'correction') = (corrects_payroll_id IS NOT NULL)),
    CONSTRAINT payrolls_number CHECK (char_length(number) BETWEEN 1 AND 40),
    CONSTRAINT payrolls_company_number_uq UNIQUE (company_id, number),
    CONSTRAINT payrolls_closed CHECK ((status = 'closed') = (closed_at IS NOT NULL))
);
-- Jedna zwykła lista na spółkę, rodzaj i miesiąc (decyzja 7.3.2).
CREATE UNIQUE INDEX payrolls_regular_uq ON payroll.payrolls (company_id, kind, period_from) WHERE kind <> 'correction';
CREATE INDEX payrolls_corrects_idx ON payroll.payrolls (corrects_payroll_id);

-- Skład listy (R3): umowy, które mogą być naliczone; excluded — wyłączone przez Kadry.
CREATE TABLE payroll.members (
    payroll_id uuid NOT NULL REFERENCES payroll.payrolls (id) ON DELETE CASCADE,
    employment_id uuid NOT NULL REFERENCES employment.employments (id),
    excluded boolean NOT NULL DEFAULT false,
    PRIMARY KEY (payroll_id, employment_id)
);
CREATE INDEX members_employment_idx ON payroll.members (employment_id);

-- Wypłata umowy na liście. Kwoty sumaryczne z pozycji. Godziny (wymiar miesiąca, przepracowane, nadgodziny) służą
-- późniejszym naliczeniom: pominięcie miesiąca w podstawie chorobowego (R7) i średnia zmiennych za urlop (R8).
CREATE TABLE payroll.entries (
    id uuid PRIMARY KEY DEFAULT uuidv7(),
    payroll_id uuid NOT NULL REFERENCES payroll.payrolls (id) ON DELETE CASCADE,
    employment_id uuid NOT NULL REFERENCES employment.employments (id),
    employee_id uuid NOT NULL REFERENCES employees.employees (id),
    terms_id uuid NULL REFERENCES employment.terms (id) ON DELETE SET NULL,
    gross numeric(12, 2) NOT NULL,
    net numeric(12, 2) NOT NULL,
    payout numeric(12, 2) NOT NULL,
    norm_hours numeric(8, 2) NULL,
    worked_hours numeric(8, 2) NULL,
    overtime_hours numeric(8, 2) NULL,
    warnings jsonb NOT NULL DEFAULT '[]',
    CONSTRAINT entries_payroll_employment_uq UNIQUE (payroll_id, employment_id)
);
CREATE INDEX entries_employee_idx ON payroll.entries (employee_id);
CREATE INDEX entries_employment_idx ON payroll.entries (employment_id);

-- Pozycja naliczenia (R24). Cechy przychodu (ZUS, zdrowotna, PIT, chorobowe) tylko dla strony income.
CREATE TABLE payroll.lines (
    id uuid PRIMARY KEY DEFAULT uuidv7(),
    entry_id uuid NOT NULL REFERENCES payroll.entries (id) ON DELETE CASCADE,
    seq integer NOT NULL,
    code text NOT NULL,
    side text NOT NULL,
    base numeric(14, 4) NULL,
    rate numeric(10, 6) NULL,
    amount numeric(12, 2) NOT NULL,
    component_type_id uuid NULL REFERENCES pay_components.component_types (id),
    in_zus_base boolean NULL,
    in_health_base boolean NULL,
    taxable boolean NULL,
    in_sickness_base boolean NULL,
    trace jsonb NOT NULL DEFAULT '{}',
    CONSTRAINT lines_side CHECK (side IN ('income', 'employee', 'employer', 'info', 'deduction')),
    CONSTRAINT lines_code CHECK (code ~ '^[A-Z0-9-]{1,20}$'),
    CONSTRAINT lines_flags CHECK (
        (side = 'income') = (in_zus_base IS NOT NULL AND in_health_base IS NOT NULL AND taxable IS NOT NULL
                             AND in_sickness_base IS NOT NULL)
    ),
    CONSTRAINT lines_entry_seq_uq UNIQUE (entry_id, seq)
);
CREATE INDEX lines_code_idx ON payroll.lines (code, entry_id);

-- Pozycja w trakcie naliczenia (silnik składa tablicę etapami, R5–R23). side = 'warning' — ostrzeżenie wypłaty.
CREATE TYPE payroll.line_t AS (
    code text,
    side text,
    base numeric,
    rate numeric,
    amount numeric,
    component_type_id uuid,
    in_zus_base boolean,
    in_health_base boolean,
    taxable boolean,
    in_sickness_base boolean,
    trace jsonb
);

-- Kontekst naliczenia jednej umowy (silnik). mode: regular albo correction (R4). zus_exclude / pit_exclude — listy
-- pomijane w sumach rocznych (naliczana lista; przy korekcie także lista korygowana i jej korekty dla ZUS).
-- ppk_date — dzień stawek PPK (przy korekcie data wypłaty listy korygowanej). costs_override — koszty przy korekcie
-- (różnica kosztów okresu i kosztów już ujętych).
CREATE TYPE payroll.ctx_t AS (
    payroll_id uuid,
    company_id uuid,
    mode text,
    employment_id uuid,
    employee_id uuid,
    contract_kind text,
    period_from date,
    period_to date,
    from_date date,
    to_date date,
    payment_date date,
    ppk_date date,
    contract_end date,
    birth_date date,
    terms_id uuid,
    pay_basis text,
    base_amount numeric,
    fte numeric,
    insurances text[],
    cost_kind text,
    zus_exclude uuid[],
    pit_exclude uuid[],
    costs_override numeric
);
