--liquibase formatted sql

--changeset hr:20261113-29
--comment: PPK (specyfikacja 008): ustawienia spółki z okresem, uczestnictwo kartoteki, okresy stawek pracownika,
--comment: deklaracje i wnioski (niezmienne), cykle ponownego zapisu co 4 lata. Kwot wpłat tu nie ma — liczy je 009.
CREATE SCHEMA ppk;
CREATE SCHEMA ppk_app;

-- Umowa o zarządzanie PPK i wpłata dodatkowa pracodawcy (R1; ustawa o PPK art. 26 ust. 2, 4). Stawki w procentach.
CREATE TABLE ppk.company_settings (
    id uuid PRIMARY KEY DEFAULT uuidv7(),
    company_id uuid NOT NULL REFERENCES platform.companies (id) ON DELETE CASCADE,
    valid_from date NOT NULL,
    valid_to date NULL,
    institution_name text NOT NULL,
    agreement_number text NOT NULL,
    agreement_date date NOT NULL,
    employer_additional_rate numeric(4, 2) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid NULL,
    CONSTRAINT company_settings_valid_from CHECK (extract(day FROM valid_from) = 1),
    CONSTRAINT company_settings_period CHECK (valid_to IS NULL OR valid_to >= valid_from),
    CONSTRAINT company_settings_texts CHECK (
        char_length(institution_name) BETWEEN 1 AND 200 AND char_length(agreement_number) BETWEEN 1 AND 60
    ),
    CONSTRAINT company_settings_rate CHECK (employer_additional_rate BETWEEN 0 AND 2.5),
    CONSTRAINT company_settings_no_overlap EXCLUDE USING gist (
        company_id WITH =, daterange(valid_from, valid_to, '[]') WITH &&
    )
);

-- Jedno uczestnictwo na kartotekę (kartoteka = spółka; R6). Stawki początkowe — okresy stawek odbudowuje
-- ppk.rebuild_rates z nich i z deklaracji zmieniających stawki.
CREATE TABLE ppk.participations (
    id uuid PRIMARY KEY DEFAULT uuidv7(),
    employee_id uuid NOT NULL REFERENCES employees.employees (id) ON DELETE CASCADE,
    enrolled_at date NOT NULL,
    institution_member_number text NULL,
    initial_basic_rate numeric(4, 2) NOT NULL,
    initial_additional_rate numeric(4, 2) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid NULL,
    CONSTRAINT participations_employee_uq UNIQUE (employee_id),
    CONSTRAINT participations_member_number CHECK (
        institution_member_number IS NULL OR char_length(institution_member_number) BETWEEN 1 AND 40
    ),
    CONSTRAINT participations_rates CHECK (
        initial_basic_rate BETWEEN 0.5 AND 2 AND initial_additional_rate BETWEEN 0 AND 2
    )
);

-- Stawki pracownika z okresem (R6, R10; art. 27 ust. 2–3). Stan pochodny: tylko ppk.rebuild_rates.
CREATE TABLE ppk.rate_periods (
    id uuid PRIMARY KEY DEFAULT uuidv7(),
    participation_id uuid NOT NULL REFERENCES ppk.participations (id) ON DELETE CASCADE,
    valid_from date NOT NULL,
    valid_to date NULL,
    employee_basic_rate numeric(4, 2) NOT NULL,
    employee_additional_rate numeric(4, 2) NOT NULL,
    CONSTRAINT rate_periods_period CHECK (valid_to IS NULL OR valid_to >= valid_from),
    CONSTRAINT rate_periods_rates CHECK (
        employee_basic_rate BETWEEN 0.5 AND 2 AND employee_additional_rate BETWEEN 0 AND 2
    ),
    CONSTRAINT rate_periods_no_overlap EXCLUDE USING gist (
        participation_id WITH =, daterange(valid_from, valid_to, '[]') WITH &&
    )
);

-- Deklaracje i wnioski (R9, R10, R13). Niezmienne; usuwane tylko pomyłki. effective_from wylicza rutyna:
-- rezygnacja — 1. dzień miesiąca złożenia, wniosek o zapis — dzień złożenia, pozostałe — 1. dzień następnego miesiąca,
-- autoRestart (wznowienie z cyklu co 4 lata) — dzień wznowienia cyklu.
CREATE TABLE ppk.declarations (
    id uuid PRIMARY KEY DEFAULT uuidv7(),
    employee_id uuid NOT NULL REFERENCES employees.employees (id) ON DELETE CASCADE,
    kind text NOT NULL,
    submitted_at date NOT NULL,
    effective_from date NOT NULL,
    rate numeric(4, 2) NULL,
    cycle_year integer NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid NULL,
    CONSTRAINT declarations_kind CHECK (
        kind IN ('resignation', 'restartRequest', 'enrollmentRequest', 'reducedRate', 'additionalRate', 'autoRestart')
    ),
    CONSTRAINT declarations_rate CHECK (
        (kind = 'reducedRate' AND rate BETWEEN 0.5 AND 2)
        OR (kind = 'additionalRate' AND rate BETWEEN 0 AND 2)
        OR (kind NOT IN ('reducedRate', 'additionalRate') AND rate IS NULL)
    ),
    CONSTRAINT declarations_cycle CHECK ((kind = 'autoRestart') = (cycle_year IS NOT NULL)),
    CONSTRAINT declarations_effective CHECK (effective_from >= date_trunc('month', submitted_at)::date)
);
CREATE INDEX declarations_employee_idx ON ppk.declarations (employee_id, effective_from);
CREATE UNIQUE INDEX declarations_auto_restart_uq ON ppk.declarations (employee_id, cycle_year)
    WHERE kind = 'autoRestart';

-- Ponowny zapis co 4 lata (R12, R13; art. 23 ust. 5–6): data przekazania informacji i wykonanie wznowienia.
CREATE TABLE ppk.reenrollment_cycles (
    company_id uuid NOT NULL REFERENCES platform.companies (id) ON DELETE CASCADE,
    year integer NOT NULL,
    informed_at date NULL,
    applied_at timestamptz NULL,
    applied_by uuid NULL,
    PRIMARY KEY (company_id, year),
    CONSTRAINT reenrollment_cycles_year CHECK (year BETWEEN 2019 AND 2100)
);
