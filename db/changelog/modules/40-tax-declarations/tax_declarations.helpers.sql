--liquibase formatted sql

--changeset hr:tax_declarations.survives_contract_end runOnChange:true splitStatements:false
--comment: Wnioski, które obowiązują po ustaniu stosunku prawnego: art. 32 ust. 6 i 8 oraz art. 41 ust. 11 u.p.d.o.f.
--comment: (art. 31a ust. 7). Pozostałe rodzaje płatnik pomija po końcu umowy (R7).
CREATE OR REPLACE FUNCTION tax_declarations.survives_contract_end(p_kind text)
    RETURNS boolean
    LANGUAGE sql
    IMMUTABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT p_kind IN ('youthReliefWaiver', 'employeeCostsWaiver', 'authorCostsWaiver');
$$;

--changeset hr:tax_declarations.natural_end runOnChange:true splitStatements:false
--comment: Najpóźniejszy koniec wpisu (R4): noAdvances — 31 grudnia roku od (art. 31c ust. 1), returnRelief — 31 grudnia
--comment: ostatniego roku ulgi; inne rodzaje bez końca (art. 31a ust. 6).
CREATE OR REPLACE FUNCTION tax_declarations.natural_end(p_kind text, p_valid_from date, p_year_to integer)
    RETURNS date
    LANGUAGE sql
    IMMUTABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT CASE p_kind
        WHEN 'noAdvances' THEN make_date(extract(year FROM p_valid_from)::integer, 12, 31)
        WHEN 'returnRelief' THEN make_date(p_year_to, 12, 31)
    END;
$$;

--changeset hr:tax_declarations.month_allowed runOnChange:true splitStatements:false
--comment: Płatnik stosuje oświadczenie najpóźniej od miesiąca po otrzymaniu (art. 31a ust. 5), więc pierwszy miesiąc
--comment: to miesiąc złożenia albo następny (R3, R6).
CREATE OR REPLACE FUNCTION tax_declarations.month_allowed(p_submitted_at date, p_month date)
    RETURNS boolean
    LANGUAGE sql
    IMMUTABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT p_month IN (
        date_trunc('month', p_submitted_at)::date,
        (date_trunc('month', p_submitted_at) + interval '1 month')::date
    );
$$;

--changeset hr:tax_declarations.lapses_on runOnChange:true splitStatements:false
--comment: Ostatni dzień stosunku prawnego, w którym wpis zaczął obowiązywać (R7). Stosunek = ciągły okres umów kartoteki
--comment: dowolnego rodzaju (przerwa co najmniej dnia go kończy); gdy w dniu od umowy nie ma — pierwszy późniejszy.
--comment: NULL: wniosek obowiązujący po końcu umowy, brak umów albo umowa bez końca.
CREATE OR REPLACE FUNCTION tax_declarations.lapses_on(p_employee_id uuid, p_kind text, p_valid_from date)
    RETURNS date
    LANGUAGE sql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    WITH periods AS (
        SELECT m.start_date AS s, coalesce(m.terminated_at, m.end_date) AS e
        FROM employment.employments m
        WHERE m.employee_id = p_employee_id AND NOT tax_declarations.survives_contract_end(p_kind)
    ), marked AS (
        SELECT s, e,
               CASE WHEN s <= max(coalesce(e, 'infinity'::date)) OVER (
                        ORDER BY s, e ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING) + 1
                    THEN 0 ELSE 1 END AS starts_island
        FROM periods
    ), islands AS (
        SELECT s, e, sum(starts_island) OVER (ORDER BY s, e) AS island FROM marked
    ), spans AS (
        SELECT min(s) AS s, CASE WHEN bool_or(e IS NULL) THEN NULL ELSE max(e) END AS e
        FROM islands GROUP BY island
    )
    SELECT spans.e FROM spans
    WHERE coalesce(spans.e, 'infinity'::date) >= p_valid_from
    ORDER BY spans.s
    LIMIT 1;
$$;

--changeset hr:tax_declarations.status runOnChange:true splitStatements:false
--comment: Status wpisu w dniu: future, active, ended (po końcu), lapsed (po końcu stosunku prawnego) (R8).
CREATE OR REPLACE FUNCTION tax_declarations.status(p_valid_from date, p_valid_to date, p_lapses_on date, p_on date)
    RETURNS text
    LANGUAGE sql
    IMMUTABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT CASE
        WHEN p_on < p_valid_from THEN 'future'
        WHEN p_lapses_on < coalesce(p_valid_to, 'infinity'::date) AND p_on > p_lapses_on THEN 'lapsed'
        WHEN p_on > p_valid_to THEN 'ended'
        ELSE 'active'
    END;
$$;

--changeset hr:tax_declarations.validate runOnChange:true splitStatements:false
--comment: Błędy pól nowego wpisu (R2–R4).
CREATE OR REPLACE FUNCTION tax_declarations.validate(
    p_kind text,
    p_share text,
    p_year_from integer,
    p_year_to integer,
    p_submitted_at date,
    p_valid_from date,
    p_valid_to date
)
    RETURNS jsonb
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_errors jsonb := '{}';
    v_end date;
BEGIN
    IF p_kind IS NULL THEN
        RETURN '{"kind": ["required"]}';
    ELSIF p_kind NOT IN ('pit2', 'youthReliefWaiver', 'returnRelief', 'familyRelief', 'seniorRelief',
        'increasedCosts', 'employeeCostsWaiver', 'authorCostsWaiver', 'noAdvances', 'higherRate') THEN
        RETURN '{"kind": ["invalid"]}';
    END IF;

    IF p_kind = 'pit2' AND p_share IS NULL THEN
        v_errors := v_errors || '{"share": ["required"]}';
    ELSIF (p_kind = 'pit2' AND p_share NOT IN ('1/12', '1/24', '1/36')) OR (p_kind <> 'pit2' AND p_share IS NOT NULL)
    THEN
        v_errors := v_errors || '{"share": ["invalid"]}';
    END IF;

    IF p_kind = 'returnRelief' THEN
        IF p_year_from IS NULL THEN
            v_errors := v_errors || '{"yearFrom": ["required"]}';
        ELSIF p_year_from NOT BETWEEN 2000 AND 2100 THEN
            v_errors := v_errors || '{"yearFrom": ["invalid"]}';
        END IF;
        IF p_year_to IS NULL THEN
            v_errors := v_errors || '{"yearTo": ["required"]}';
        ELSIF p_year_to NOT BETWEEN 2000 AND 2100 THEN
            v_errors := v_errors || '{"yearTo": ["invalid"]}';
        ELSIF p_year_to < p_year_from THEN
            v_errors := v_errors || '{"yearTo": ["beforeFrom"]}';
        END IF;
    ELSE
        IF p_year_from IS NOT NULL THEN
            v_errors := v_errors || '{"yearFrom": ["invalid"]}';
        END IF;
        IF p_year_to IS NOT NULL THEN
            v_errors := v_errors || '{"yearTo": ["invalid"]}';
        END IF;
    END IF;

    IF p_submitted_at IS NULL THEN
        v_errors := v_errors || '{"submittedAt": ["required"]}';
    ELSIF p_submitted_at > current_date THEN
        v_errors := v_errors || '{"submittedAt": ["future"]}';
    END IF;

    IF p_valid_from IS NULL THEN
        RETURN v_errors || '{"validFrom": ["required"]}';
    ELSIF extract(day FROM p_valid_from) <> 1 THEN
        RETURN v_errors || '{"validFrom": ["invalid"]}';
    ELSIF p_submitted_at IS NOT NULL AND (
        NOT tax_declarations.month_allowed(p_submitted_at, p_valid_from)
        OR (p_kind = 'noAdvances' AND extract(year FROM p_valid_from) <> extract(year FROM p_submitted_at))
        OR (p_kind = 'returnRelief' AND p_year_to >= p_year_from
            AND extract(year FROM p_valid_from) NOT BETWEEN p_year_from AND p_year_to)
    ) THEN
        v_errors := v_errors || '{"validFrom": ["outsideAllowed"]}';
    END IF;

    IF p_valid_to IS NOT NULL THEN
        v_end := tax_declarations.natural_end(p_kind, p_valid_from, p_year_to);
        IF extract(day FROM p_valid_to + 1) <> 1 OR p_valid_to > v_end THEN
            v_errors := v_errors || '{"validTo": ["invalid"]}';
        ELSIF p_valid_to < p_valid_from THEN
            v_errors := v_errors || '{"validTo": ["beforeFrom"]}';
        END IF;
    END IF;
    RETURN v_errors;
END;
$$;

--changeset hr:tax_declarations.in_force runOnChange:true splitStatements:false
--comment: Wpisy kartoteki obowiązujące w dniu — z końcem i wygasaniem po końcu umowy (R11). Dla listy płac (009).
CREATE OR REPLACE FUNCTION tax_declarations.in_force(p_employee_id uuid, p_on date)
    RETURNS SETOF tax_declarations.declarations
    LANGUAGE sql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT d.* FROM tax_declarations.declarations d
    WHERE d.employee_id = p_employee_id
      AND tax_declarations.status(d.valid_from, d.valid_to,
          tax_declarations.lapses_on(d.employee_id, d.kind, d.valid_from), p_on) = 'active';
$$;
