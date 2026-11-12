--liquibase formatted sql

--changeset hr:employment.period runOnChange:true splitStatements:false
--comment: Okres umowy: od początku do rozwiązania, bez rozwiązania do końca; bez końca — otwarty (R4).
CREATE OR REPLACE FUNCTION employment.period(p_start date, p_end date, p_terminated date)
    RETURNS daterange
    LANGUAGE sql
    IMMUTABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT daterange(p_start, coalesce(p_terminated, p_end), '[]');
$$;

--changeset hr:employment.status runOnChange:true splitStatements:false
--comment: Status umowy w dniu: future, active, ended (R8).
CREATE OR REPLACE FUNCTION employment.status(p_start date, p_end date, p_terminated date, p_on date)
    RETURNS text
    LANGUAGE sql
    IMMUTABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT CASE
        WHEN p_on < p_start THEN 'future'
        WHEN coalesce(p_terminated, p_end) IS NOT NULL AND p_on > coalesce(p_terminated, p_end) THEN 'ended'
        ELSE 'active'
    END;
$$;

--changeset hr:employment.retention_until runOnChange:true splitStatements:false
--comment: Do kiedy przechowywać dokumentację umowy (R17); 'infinity', gdy umowa nie ma daty zakończenia.
--comment: Umowa o pracę i zlecenie od 2019: 10 lat od końca roku zakończenia (KP art. 94 pkt 9b; ustawa o emeryturach
--comment: i rentach z FUS art. 125a ust. 4a). Przed 2019: 50 lat od zakończenia pracy (art. 125a ust. 4; ustawa
--comment: z 10.01.2018, Dz.U. 2018 poz. 357, art. 7 ust. 2) — bez skracania raportem ZUS RIA. Dzieło: założenie
--comment: zachowawcze jak zlecenie od 2019, do potwierdzenia ze źródłem w module 010.
CREATE OR REPLACE FUNCTION employment.retention_until(p_kind text, p_start date, p_end date, p_terminated date)
    RETURNS date
    LANGUAGE sql
    IMMUTABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT CASE
        WHEN coalesce(p_terminated, p_end) IS NULL THEN 'infinity'::date
        WHEN p_kind = 'specific_work' OR p_start >= DATE '2019-01-01'
            THEN make_date(extract(year FROM coalesce(p_terminated, p_end))::integer + 10, 12, 31)
        ELSE (coalesce(p_terminated, p_end) + interval '50 years')::date
    END;
$$;

--changeset hr:employment.employee_status runOnChange:true splitStatements:false
--comment: Status kartoteki w dniu z jej umów: active, future, former, none (R16).
CREATE OR REPLACE FUNCTION employment.employee_status(p_employee_id uuid, p_on date)
    RETURNS text
    LANGUAGE sql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT CASE
        WHEN bool_or(s = 'active') THEN 'active'
        WHEN bool_or(s = 'future') THEN 'future'
        WHEN count(*) > 0 THEN 'former'
        ELSE 'none'
    END
    FROM (
        SELECT employment.status(m.start_date, m.end_date, m.terminated_at, p_on) AS s
        FROM employment.employments m
        WHERE m.employee_id = p_employee_id
    ) x;
$$;

--changeset hr:employment.employee_retention_until runOnChange:true splitStatements:false
--comment: Najpóźniejszy koniec przechowywania umów kartoteki; NULL, gdy umów nie ma; 'infinity' przy umowie otwartej.
CREATE OR REPLACE FUNCTION employment.employee_retention_until(p_employee_id uuid)
    RETURNS date
    LANGUAGE sql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT max(employment.retention_until(m.kind, m.start_date, m.end_date, m.terminated_at))
    FROM employment.employments m
    WHERE m.employee_id = p_employee_id;
$$;

--changeset hr:employment.employment_validate runOnChange:true splitStatements:false
--comment: Błędy pól umowy (R1–R4). p_id = NULL przy nowej umowie.
CREATE OR REPLACE FUNCTION employment.employment_validate(
    p_id uuid,
    p_employee_id uuid,
    p_company_id uuid,
    p_number text,
    p_kind text,
    p_contract_type text,
    p_signed_at date,
    p_start date,
    p_end date,
    p_terminated date
)
    RETURNS jsonb
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_errors jsonb := '{}';
BEGIN
    IF p_number IS NULL THEN
        v_errors := v_errors || '{"number": ["required"]}';
    ELSIF char_length(p_number) > 40 THEN
        v_errors := v_errors || '{"number": ["maxLength"]}';
    ELSIF EXISTS (
        SELECT FROM employment.employments m
        WHERE m.company_id = p_company_id AND m.number = p_number AND m.id IS DISTINCT FROM p_id
    ) THEN
        v_errors := v_errors || '{"number": ["duplicate"]}';
    END IF;

    IF p_kind IS NULL OR p_kind NOT IN ('employment', 'mandate', 'specific_work') THEN
        RETURN v_errors || '{"kind": ["invalid"]}';
    END IF;
    IF p_kind = 'employment' AND p_contract_type IS NULL THEN
        v_errors := v_errors || '{"contractType": ["required"]}';
    ELSIF (p_kind = 'employment' AND p_contract_type NOT IN ('trial', 'fixed_term', 'indefinite'))
        OR (p_kind <> 'employment' AND p_contract_type IS NOT NULL) THEN
        v_errors := v_errors || '{"contractType": ["invalid"]}';
    END IF;

    IF p_signed_at IS NULL THEN
        v_errors := v_errors || '{"signedAt": ["required"]}';
    END IF;
    IF p_start IS NULL THEN
        RETURN v_errors || '{"startDate": ["required"]}';
    END IF;
    IF p_signed_at > p_start THEN
        v_errors := v_errors || '{"signedAt": ["afterStart"]}';
    END IF;

    IF p_contract_type = 'indefinite' AND p_end IS NOT NULL THEN
        v_errors := v_errors || '{"endDate": ["invalid"]}';
    ELSIF coalesce(p_contract_type, '') <> 'indefinite' AND p_end IS NULL THEN
        v_errors := v_errors || '{"endDate": ["required"]}';
    ELSIF p_end < p_start THEN
        v_errors := v_errors || '{"endDate": ["beforeFrom"]}';
    ELSIF p_terminated IS NOT NULL AND (p_terminated < p_start OR p_terminated > p_end) THEN
        v_errors := v_errors || '{"endDate": ["beforeTermination"]}';
    END IF;

    IF p_kind = 'employment' AND v_errors = '{}' AND EXISTS (
        SELECT FROM employment.employments m
        WHERE m.employee_id = p_employee_id AND m.kind = 'employment' AND m.id IS DISTINCT FROM p_id
          AND employment.period(m.start_date, m.end_date, m.terminated_at)
              && employment.period(p_start, p_end, p_terminated)
    ) THEN
        v_errors := v_errors || '{"startDate": ["overlap"]}';
    END IF;
    RETURN v_errors;
END;
$$;

--changeset hr:employment.check_concurrency runOnChange:true splitStatements:false
--comment: Zlecenie albo dzieło w okresie umowy o pracę tej samej kartoteki (umowa z własnym pracownikiem) — w pierwszej
--comment: wersji blokada U0003 z powodem concurrentTitleUnsupported (decyzja 7.3.1, R5).
CREATE OR REPLACE FUNCTION employment.check_concurrency(
    p_employee_id uuid,
    p_kind text,
    p_period daterange,
    p_except_id uuid
)
    RETURNS void
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    IF EXISTS (
        SELECT FROM employment.employments m
        WHERE m.employee_id = p_employee_id AND m.id IS DISTINCT FROM p_except_id
          AND ((p_kind = 'employment') <> (m.kind = 'employment'))
          AND employment.period(m.start_date, m.end_date, m.terminated_at) && p_period
    ) THEN
        RAISE EXCEPTION 'concurrent insurance title with own employee is not supported yet'
            USING ERRCODE = 'U0003', HINT = 'concurrentTitleUnsupported';
    END IF;
END;
$$;

--changeset hr:employment.zus_code_id runOnChange:true splitStatements:false
--comment: Id kodu ZUS, który istnieje i nie wygasł przed dniem (R11). Słownik ma kody z załącznika z 2026 r., więc
--comment: początek obowiązywania kodu nie jest sprawdzany — wcześniejsze umowy używają tych samych kodów.
CREATE OR REPLACE FUNCTION employment.zus_code_id(p_kind text, p_code text, p_on date)
    RETURNS uuid
    LANGUAGE sql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT z.id FROM dictionaries.zus_codes z
    WHERE z.kind = p_kind AND z.code = p_code AND (z.valid_to IS NULL OR z.valid_to >= p_on);
$$;

--changeset hr:employment.terms_validate runOnChange:true splitStatements:false
--comment: Błędy pól wersji warunków zależnie od rodzaju umowy (R10–R13). p_current_position_id: stanowisko z
--comment: poprzedniej wersji — nieaktywne stanowisko wolno zostawić, nowego nieaktywnego nie.
CREATE OR REPLACE FUNCTION employment.terms_validate(
    p_kind text,
    p_valid_from date,
    p_position_id uuid,
    p_current_position_id uuid,
    p_fte_numerator integer,
    p_fte_denominator integer,
    p_pay_basis text,
    p_base_amount numeric,
    p_zus_title_code text,
    p_zus_pension_code text,
    p_zus_disability_code text,
    p_insurances text[],
    p_cost_kind text
)
    RETURNS jsonb
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_errors jsonb := '{}';
    v_ins text[] := coalesce(p_insurances, '{}');
    v_minimum numeric;
    v_codes_given boolean := p_zus_title_code IS NOT NULL OR p_zus_pension_code IS NOT NULL
        OR p_zus_disability_code IS NOT NULL;
BEGIN
    IF p_valid_from IS NULL THEN
        v_errors := v_errors || '{"validFrom": ["required"]}';
    END IF;

    -- Stanowisko i etat: tylko umowa o pracę.
    IF p_kind = 'employment' THEN
        IF p_position_id IS NULL THEN
            v_errors := v_errors || '{"positionId": ["required"]}';
        ELSIF NOT EXISTS (SELECT FROM dictionaries.positions p WHERE p.id = p_position_id) THEN
            v_errors := v_errors || '{"positionId": ["notFound"]}';
        ELSIF p_position_id IS DISTINCT FROM p_current_position_id
            AND NOT EXISTS (SELECT FROM dictionaries.positions p WHERE p.id = p_position_id AND p.is_active) THEN
            v_errors := v_errors || '{"positionId": ["inactive"]}';
        END IF;
        IF p_fte_numerator IS NULL OR p_fte_denominator IS NULL THEN
            v_errors := v_errors || '{"fteNumerator": ["required"]}';
        ELSIF p_fte_numerator <= 0 OR p_fte_denominator NOT BETWEEN 1 AND 1000
            OR p_fte_numerator > p_fte_denominator THEN
            v_errors := v_errors || '{"fteNumerator": ["invalid"]}';
        END IF;
    ELSE
        IF p_position_id IS NOT NULL THEN
            v_errors := v_errors || '{"positionId": ["invalid"]}';
        END IF;
        IF p_fte_numerator IS NOT NULL OR p_fte_denominator IS NOT NULL THEN
            v_errors := v_errors || '{"fteNumerator": ["invalid"]}';
        END IF;
    END IF;

    IF p_pay_basis IS NULL THEN
        v_errors := v_errors || '{"payBasis": ["required"]}';
    ELSIF (p_kind = 'employment' AND p_pay_basis <> 'monthly')
        OR (p_kind = 'mandate' AND p_pay_basis NOT IN ('hourly', 'lumpSum'))
        OR (p_kind = 'specific_work' AND p_pay_basis <> 'lumpSum') THEN
        v_errors := v_errors || '{"payBasis": ["invalid"]}';
    END IF;

    IF p_cost_kind IS NULL THEN
        v_errors := v_errors || '{"costKind": ["required"]}';
    ELSIF (p_kind = 'employment' AND p_cost_kind NOT IN ('standard', 'increased'))
        OR (p_kind <> 'employment' AND p_cost_kind NOT IN ('flat20', 'author50')) THEN
        v_errors := v_errors || '{"costKind": ["invalid"]}';
    END IF;

    -- Kody ZUS: umowa o pracę i zlecenie — wymagane; dzieło — nie jest tytułem do ubezpieczeń.
    IF p_kind = 'specific_work' THEN
        IF v_codes_given THEN
            v_errors := v_errors || '{"zusTitleCode": ["invalid"]}';
        END IF;
    ELSIF p_valid_from IS NOT NULL THEN
        IF p_zus_title_code IS NULL THEN
            v_errors := v_errors || '{"zusTitleCode": ["required"]}';
        ELSIF employment.zus_code_id('title', p_zus_title_code, p_valid_from) IS NULL THEN
            v_errors := v_errors || '{"zusTitleCode": ["notFound"]}';
        END IF;
        IF p_zus_pension_code IS NULL THEN
            v_errors := v_errors || '{"zusPensionCode": ["required"]}';
        ELSIF employment.zus_code_id('pension', p_zus_pension_code, p_valid_from) IS NULL THEN
            v_errors := v_errors || '{"zusPensionCode": ["notFound"]}';
        END IF;
        IF p_zus_disability_code IS NULL THEN
            v_errors := v_errors || '{"zusDisabilityCode": ["required"]}';
        ELSIF employment.zus_code_id('disability', p_zus_disability_code, p_valid_from) IS NULL THEN
            v_errors := v_errors || '{"zusDisabilityCode": ["notFound"]}';
        END IF;
    END IF;

    -- Ubezpieczenia (R12).
    IF NOT v_ins <@ ARRAY['pension', 'disability', 'sickness', 'accident', 'health', 'fp', 'fgsp'] THEN
        v_errors := v_errors || '{"insurances": ["invalid"]}';
    ELSIF p_kind = 'employment' THEN
        IF NOT v_ins @> ARRAY['pension', 'disability', 'sickness', 'accident', 'health'] THEN
            v_errors := v_errors || '{"insurances": ["missingMandatory"]}';
        END IF;
    ELSIF p_kind = 'mandate' THEN
        IF ('pension' = ANY (v_ins)) <> ('disability' = ANY (v_ins))
            OR (NOT 'pension' = ANY (v_ins) AND v_ins && ARRAY['sickness', 'accident', 'fp', 'fgsp']) THEN
            v_errors := v_errors || '{"insurances": ["invalid"]}';
        END IF;
    ELSIF cardinality(v_ins) > 0 THEN
        v_errors := v_errors || '{"insurances": ["invalid"]}';
    END IF;

    -- Kwota i płaca minimalna (R13); bez parametru na ten dzień sprawdzenia nie ma.
    IF p_base_amount IS NULL THEN
        v_errors := v_errors || '{"baseAmount": ["required"]}';
    ELSIF p_base_amount <= 0 OR p_base_amount >= 10000000000 OR p_base_amount <> round(p_base_amount, 2) THEN
        v_errors := v_errors || '{"baseAmount": ["invalid"]}';
    ELSIF p_valid_from IS NOT NULL AND p_kind = 'employment' AND p_pay_basis = 'monthly'
        AND p_fte_numerator > 0 AND p_fte_denominator > 0 THEN
        v_minimum := platform.legal_parameter_or_null('placa.minimalna', p_valid_from);
        IF p_base_amount * p_fte_denominator < v_minimum * p_fte_numerator THEN
            v_errors := v_errors || '{"baseAmount": ["belowMinimum"]}';
        END IF;
    ELSIF p_valid_from IS NOT NULL AND p_kind = 'mandate' AND p_pay_basis = 'hourly' THEN
        v_minimum := platform.legal_parameter_or_null('placa.minimalna.godzinowa', p_valid_from);
        IF p_base_amount < v_minimum THEN
            v_errors := v_errors || '{"baseAmount": ["belowMinimum"]}';
        END IF;
    END IF;
    RETURN v_errors;
END;
$$;

--changeset hr:employment.normalize_insurances runOnChange:true splitStatements:false
--comment: Ubezpieczenia bez powtórzeń, w stałej kolejności (audyt nie pokazuje zmian samej kolejności).
CREATE OR REPLACE FUNCTION employment.normalize_insurances(p_insurances text[])
    RETURNS text[]
    LANGUAGE sql
    IMMUTABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT coalesce(array_agg(x ORDER BY array_position(
        ARRAY['pension', 'disability', 'sickness', 'accident', 'health', 'fp', 'fgsp'], x)), '{}')
    FROM (SELECT DISTINCT unnest(p_insurances) AS x) d;
$$;
