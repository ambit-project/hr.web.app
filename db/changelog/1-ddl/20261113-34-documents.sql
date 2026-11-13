--liquibase formatted sql

--changeset hr:20261113-34
--comment: Dokumenty i PIT (specyfikacja 010): informacje PIT-11 z wersjami (złożenie, korekta). Paski, karta
--comment: wynagrodzeń, zestawienie PIT-4R/8AR i dokument zgody nie mają tabel — powstają z zamkniętych list 009 i danych
--comment: 006 przy odczycie.
CREATE SCHEMA documents;
CREATE SCHEMA documents_app;

-- PIT-11(29) kartoteki za rok (R5–R10). data — pozycje formularza pod nazwami elementów XSD (P_29 …) i dane
-- identyfikacyjne z dnia wygenerowania (płatnik, podatnik bez PESEL, adres zamieszkania, urząd skarbowy), listy
-- źródłowe i brakujące dane (missing). PESEL nie trafia do data — jest zaszyfrowany w kartotece (ADR-0009) i czyta
-- się go przy odczycie formularza. Wersja niewysłana jest zastępowana przy ponownym generowaniu; po oznaczeniu
-- wysłania zmiana to nowa wersja z celem correction (R7 [D]).
CREATE TABLE documents.tax_forms (
    id uuid PRIMARY KEY DEFAULT uuidv7(),
    company_id uuid NOT NULL REFERENCES platform.companies (id),
    employee_id uuid NOT NULL REFERENCES employees.employees (id),
    year integer NOT NULL,
    version integer NOT NULL,
    purpose text NOT NULL,
    data jsonb NOT NULL,
    generated_at timestamptz NOT NULL DEFAULT now(),
    generated_by uuid NULL,
    sent_to_office_at date NULL,
    given_to_employee_at date NULL,
    given_how text NULL,
    CONSTRAINT tax_forms_purpose CHECK (purpose IN ('filing', 'correction')),
    CONSTRAINT tax_forms_version CHECK (version >= 1 AND (version = 1) = (purpose = 'filing')),
    CONSTRAINT tax_forms_year CHECK (year BETWEEN 2024 AND 2100),
    CONSTRAINT tax_forms_given CHECK ((given_to_employee_at IS NULL) = (given_how IS NULL)),
    CONSTRAINT tax_forms_given_how CHECK (given_how IN ('paper', 'email', 'portal')),
    CONSTRAINT tax_forms_employee_year_version_uq UNIQUE (employee_id, year, version)
);
CREATE INDEX tax_forms_company_year_idx ON documents.tax_forms (company_id, year);
