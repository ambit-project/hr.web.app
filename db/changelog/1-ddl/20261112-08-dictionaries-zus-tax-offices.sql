--liquibase formatted sql

--changeset hr:20261112-08
--comment: Słowniki kodów ZUS (tytuł ubezpieczenia, prawo do emerytury/renty, niepełnosprawność) i urzędów skarbowych.
CREATE TABLE dictionaries.zus_codes (
    id uuid PRIMARY KEY DEFAULT uuidv7(),
    kind text NOT NULL,
    code text NOT NULL,
    name text NOT NULL,
    valid_from date NOT NULL,
    valid_to date NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid NULL,
    CONSTRAINT zus_codes_kind CHECK (kind IN ('title', 'pension', 'disability')),
    CONSTRAINT zus_codes_code_format CHECK (
        (kind = 'title' AND code ~ '^\d{4}$') OR (kind IN ('pension', 'disability') AND code ~ '^\d$')
    ),
    CONSTRAINT zus_codes_name_length CHECK (char_length(name) BETWEEN 1 AND 2000),
    CONSTRAINT zus_codes_period CHECK (valid_to IS NULL OR valid_to >= valid_from),
    CONSTRAINT zus_codes_kind_code_uq UNIQUE (kind, code)
);

CREATE TABLE dictionaries.tax_offices (
    id uuid PRIMARY KEY DEFAULT uuidv7(),
    code text NOT NULL,
    name text NOT NULL,
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid NULL,
    CONSTRAINT tax_offices_code_format CHECK (code ~ '^\d{4}$'),
    CONSTRAINT tax_offices_name_length CHECK (char_length(name) BETWEEN 1 AND 300),
    CONSTRAINT tax_offices_code_uq UNIQUE (code)
);
