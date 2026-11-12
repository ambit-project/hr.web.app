--liquibase formatted sql

--changeset hr:20261112-06
--comment: Parametry prawne z okresem obowiązywania; okresy jednego kodu się nie nakładają.
CREATE TABLE platform.legal_parameters (
    id uuid PRIMARY KEY DEFAULT uuidv7(),
    code text NOT NULL,
    value numeric(18, 6) NOT NULL,
    valid_from date NOT NULL,
    valid_to date NULL,
    source text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid NULL,
    CONSTRAINT legal_parameters_code_format CHECK (code ~ '^[a-z0-9]+(\.[a-z0-9]+)*$'),
    CONSTRAINT legal_parameters_period CHECK (valid_to IS NULL OR valid_to >= valid_from),
    CONSTRAINT legal_parameters_no_overlap EXCLUDE USING gist (
        code WITH =,
        daterange(valid_from, valid_to, '[]') WITH &&
    )
);
