--liquibase formatted sql

--changeset hr:20261112-09
--comment: Urząd skarbowy płatnika (PIT-4R, PIT-8AR). NULL tylko dla spółek założonych przed modułem 002.
ALTER TABLE platform.companies
    ADD COLUMN tax_office_id uuid NULL REFERENCES dictionaries.tax_offices (id);
CREATE INDEX companies_tax_office_idx ON platform.companies (tax_office_id);
