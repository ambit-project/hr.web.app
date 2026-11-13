--liquibase formatted sql

--changeset hr:20261113-26
--comment: Czas pracy i nieobecności: święta, kalendarze spółek, rodzaje nieobecności, nieobecności, limity urlopu,
--comment: nadgodziny i godziny zlecenia (specyfikacja 007). Dni i godziny nieobecności liczą funkcje przy odczycie.
CREATE SCHEMA time_absence;
CREATE SCHEMA time_absence_app;

-- Święta ustawowe (ustawa o dniach wolnych od pracy art. 1 pkt 1; R1).
CREATE TABLE time_absence.holidays (
    date date PRIMARY KEY,
    name text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid NULL,
    CONSTRAINT holidays_name CHECK (char_length(name) BETWEEN 1 AND 100)
);

-- Kalendarz spółki = dni roku; „rok ma kalendarz” = są dni tej spółki w tym roku (R2–R4).
CREATE TABLE time_absence.calendar_days (
    company_id uuid NOT NULL REFERENCES platform.companies (id) ON DELETE CASCADE,
    date date NOT NULL,
    kind text NOT NULL,
    hours numeric(4, 2) NOT NULL,
    PRIMARY KEY (company_id, date),
    CONSTRAINT calendar_days_kind CHECK (kind IN ('workday', 'dayOff', 'holiday', 'designatedOff')),
    CONSTRAINT calendar_days_hours CHECK (
        (kind = 'workday' AND hours BETWEEN 0 AND 12) OR (kind <> 'workday' AND hours = 0)
    )
);

-- Słownik systemowy rodzajów nieobecności (R6), tylko do odczytu.
CREATE TABLE time_absence.absence_types (
    code text PRIMARY KEY,
    name text NOT NULL,
    payment text NOT NULL,
    limit_kind text NOT NULL,
    limit_days integer NULL,
    sickness boolean NOT NULL,
    legal_basis text NULL,
    sort_order integer NOT NULL,
    CONSTRAINT absence_types_payment CHECK (
        payment IN ('leavePay', 'salary', 'salaryHalf', 'sickness80', 'sickness100', 'unpaid')
    ),
    -- leave: limit urlopu (UZ dodatkowo limit_days w roku); yearDays: dni w roku; event: dni na jedno zdarzenie.
    CONSTRAINT absence_types_limit CHECK (
        (limit_kind = 'none' AND limit_days IS NULL)
        OR (limit_kind = 'leave' AND (limit_days IS NULL OR limit_days > 0))
        OR (limit_kind IN ('yearDays', 'event') AND limit_days > 0)
    )
);

CREATE TABLE time_absence.absences (
    id uuid PRIMARY KEY DEFAULT uuidv7(),
    employment_id uuid NOT NULL REFERENCES employment.employments (id) ON DELETE CASCADE,
    absence_type_code text NOT NULL REFERENCES time_absence.absence_types (code),
    date_from date NOT NULL,
    date_to date NOT NULL,
    certificate_number text NULL,
    note text NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid NULL,
    CONSTRAINT absences_period CHECK (date_to >= date_from),
    CONSTRAINT absences_texts CHECK (
        (certificate_number IS NULL OR char_length(certificate_number) BETWEEN 1 AND 20)
        AND (note IS NULL OR char_length(note) <= 2000)
    ),
    CONSTRAINT absences_no_overlap EXCLUDE USING gist (
        employment_id WITH =, daterange(date_from, date_to, '[]') WITH &&
    )
);

-- Limit urlopu umowy o pracę na rok (R13, R14). Puste godziny = wyliczane (propozycja, zaległy z poprzedniego roku).
CREATE TABLE time_absence.leave_entitlements (
    id uuid PRIMARY KEY DEFAULT uuidv7(),
    employment_id uuid NOT NULL REFERENCES employment.employments (id) ON DELETE CASCADE,
    year integer NOT NULL,
    base_days integer NOT NULL,
    first_job boolean NOT NULL DEFAULT false,
    entitled_hours numeric(6, 2) NULL,
    carried_over_hours numeric(6, 2) NULL,
    override_reason text NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid NULL,
    CONSTRAINT leave_entitlements_year CHECK (year BETWEEN 2000 AND 2100),
    CONSTRAINT leave_entitlements_base_days CHECK (base_days IN (20, 26)),
    CONSTRAINT leave_entitlements_hours CHECK (
        (entitled_hours IS NULL OR entitled_hours >= 0) AND (carried_over_hours IS NULL OR carried_over_hours >= 0)
    ),
    CONSTRAINT leave_entitlements_reason CHECK (
        (entitled_hours IS NULL AND carried_over_hours IS NULL AND override_reason IS NULL)
        OR ((entitled_hours IS NOT NULL OR carried_over_hours IS NOT NULL)
            AND char_length(override_reason) BETWEEN 1 AND 500)
    ),
    CONSTRAINT leave_entitlements_uq UNIQUE (employment_id, year)
);

-- Nadgodziny umowy o pracę (R16).
CREATE TABLE time_absence.overtime_entries (
    id uuid PRIMARY KEY DEFAULT uuidv7(),
    employment_id uuid NOT NULL REFERENCES employment.employments (id) ON DELETE CASCADE,
    date date NOT NULL,
    hours numeric(4, 2) NOT NULL,
    rate integer NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid NULL,
    CONSTRAINT overtime_entries_hours CHECK (hours BETWEEN 0.5 AND 16),
    CONSTRAINT overtime_entries_rate CHECK (rate IN (50, 100)),
    CONSTRAINT overtime_entries_uq UNIQUE (employment_id, date, rate)
);

-- Godziny zlecenia ze stawką godzinową i ich potwierdzenie (R17; ustawa o minimalnym wynagrodzeniu art. 8b).
CREATE TABLE time_absence.mandate_hours (
    id uuid PRIMARY KEY DEFAULT uuidv7(),
    employment_id uuid NOT NULL REFERENCES employment.employments (id) ON DELETE CASCADE,
    date_from date NOT NULL,
    date_to date NOT NULL,
    hours numeric(6, 2) NOT NULL,
    confirmed_at date NULL,
    confirmation_form text NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid NULL,
    CONSTRAINT mandate_hours_period CHECK (date_to >= date_from),
    CONSTRAINT mandate_hours_hours CHECK (hours > 0 AND hours <= 744),
    CONSTRAINT mandate_hours_confirmation CHECK (
        (confirmed_at IS NULL AND confirmation_form IS NULL)
        OR (confirmed_at IS NOT NULL AND confirmation_form IN ('written', 'electronic', 'documentary'))
    ),
    CONSTRAINT mandate_hours_no_overlap EXCLUDE USING gist (
        employment_id WITH =, daterange(date_from, date_to, '[]') WITH &&
    )
);
