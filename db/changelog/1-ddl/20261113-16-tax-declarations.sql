--liquibase formatted sql

--changeset hr:20261113-16
--comment: Oświadczenia i wnioski podatkowe pracownika z okresem obowiązywania w miesiącach (specyfikacja 005).
CREATE SCHEMA tax_declarations;
CREATE SCHEMA tax_declarations_app;

CREATE TABLE tax_declarations.declarations (
    id uuid PRIMARY KEY DEFAULT uuidv7(),
    employee_id uuid NOT NULL REFERENCES employees.employees (id) ON DELETE CASCADE,
    kind text NOT NULL,
    share text NULL,
    year_from integer NULL,
    year_to integer NULL,
    submitted_at date NOT NULL,
    valid_from date NOT NULL,
    valid_to date NULL,
    withdrawn_at date NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid NULL,
    CONSTRAINT declarations_kind CHECK (kind IN (
        'pit2', 'youthReliefWaiver', 'returnRelief', 'familyRelief', 'seniorRelief', 'increasedCosts',
        'employeeCostsWaiver', 'authorCostsWaiver', 'noAdvances', 'higherRate'
    )),
    CONSTRAINT declarations_share CHECK (
        (kind = 'pit2' AND share IN ('1/12', '1/24', '1/36')) OR (kind <> 'pit2' AND share IS NULL)
    ),
    CONSTRAINT declarations_years CHECK (
        (kind = 'returnRelief' AND year_from IS NOT NULL AND year_to >= year_from)
        OR (kind <> 'returnRelief' AND year_from IS NULL AND year_to IS NULL)
    ),
    CONSTRAINT declarations_months CHECK (
        extract(day FROM valid_from) = 1
        AND (valid_to IS NULL OR (valid_to >= valid_from AND extract(day FROM valid_to + 1) = 1))
    ),
    -- Dwa wpisy tego samego rodzaju w kartotece nie nakładają się (R5).
    CONSTRAINT declarations_no_overlap EXCLUDE USING gist (
        employee_id WITH =, kind WITH =, daterange(valid_from, valid_to, '[]') WITH &&
    )
);
CREATE INDEX declarations_employee_idx ON tax_declarations.declarations (employee_id);
