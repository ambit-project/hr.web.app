--liquibase formatted sql

--changeset hr:20261112-02
--comment: Słownik stanowisk.
CREATE TABLE dictionaries.positions (
    id uuid PRIMARY KEY DEFAULT uuidv7(),
    name text NOT NULL,
    description text NULL,
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid NULL,
    CONSTRAINT positions_name_length CHECK (char_length(name) BETWEEN 1 AND 200),
    CONSTRAINT positions_description_length CHECK (char_length(description) <= 1000)
);

CREATE UNIQUE INDEX positions_name_uq ON dictionaries.positions (lower(name));
