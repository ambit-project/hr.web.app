--liquibase formatted sql

--changeset hr:20261112-05
--comment: Spółki grupy (pracodawcy i płatnicy). NIP i REGON zapisane jako same cyfry.
CREATE TABLE platform.companies (
    id uuid PRIMARY KEY DEFAULT uuidv7(),
    short_name text NOT NULL,
    name text NOT NULL,
    nip text NOT NULL,
    regon text NOT NULL,
    street text NOT NULL,
    postal_code text NOT NULL,
    city text NOT NULL,
    country text NOT NULL DEFAULT 'PL',
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid NULL,
    CONSTRAINT companies_nip_digits CHECK (nip ~ '^\d{10}$'),
    CONSTRAINT companies_regon_digits CHECK (regon ~ '^(\d{9}|\d{14})$'),
    CONSTRAINT companies_postal_code CHECK (postal_code ~ '^\d{2}-\d{3}$')
);
CREATE UNIQUE INDEX companies_short_name_uq ON platform.companies (lower(short_name));
CREATE UNIQUE INDEX companies_nip_uq ON platform.companies (nip);
