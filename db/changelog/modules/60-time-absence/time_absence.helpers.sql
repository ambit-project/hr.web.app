--liquibase formatted sql

--changeset hr:time_absence.employment_get runOnChange:true splitStatements:false
--comment: Umowa do operacji modułu: U0002, gdy nie ma; U0003 kindNotAllowed, gdy rodzaj umowy nie pasuje (np. limit
--comment: urlopu i nadgodziny tylko umowa o pracę, godziny tylko zlecenie). Blokadę wiersza zakłada rutyna zapisu.
CREATE OR REPLACE FUNCTION time_absence.employment_get(p_employment_id uuid, p_kinds text[])
    RETURNS employment.employments
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_row employment.employments%ROWTYPE;
BEGIN
    SELECT * INTO v_row FROM employment.employments m WHERE m.id = p_employment_id;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'employment not found' USING ERRCODE = 'U0002';
    END IF;
    IF NOT v_row.kind = ANY (p_kinds) THEN
        RAISE EXCEPTION 'operation not allowed for this kind of contract' USING ERRCODE = 'U0003',
            HINT = 'kindNotAllowed';
    END IF;
    RETURN v_row;
END;
$$;

--changeset hr:time_absence.require_employee_access runOnChange:true splitStatements:false
--comment: Kadry i Administrator — każda kartoteka; Pracownik — tylko powiązana z kontem (R15, R19).
CREATE OR REPLACE FUNCTION time_absence.require_employee_access(p_employee_id uuid)
    RETURNS void
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    IF NOT EXISTS (
        SELECT FROM platform.user_roles r JOIN platform.users u ON u.id = r.user_id
        WHERE r.user_id = platform.actor_id() AND u.is_active AND r.role IN ('kadry', 'administrator')
    ) THEN
        PERFORM platform.require_role('pracownik');
        IF NOT EXISTS (
            SELECT FROM platform.user_employee_links l
            WHERE l.user_id = platform.actor_id() AND l.employee_id = p_employee_id
        ) THEN
            RAISE EXCEPTION 'forbidden' USING ERRCODE = 'U0004';
        END IF;
    END IF;
    IF NOT EXISTS (SELECT FROM employees.employees e WHERE e.id = p_employee_id) THEN
        RAISE EXCEPTION 'employee not found' USING ERRCODE = 'U0002';
    END IF;
END;
$$;

--changeset hr:time_absence.working_hours runOnChange:true splitStatements:false
--comment: Godziny pracy umowy w okresie (R5, R9): suma godzin dni roboczych kalendarza spółki w okresie umowy × etat
--comment: z warunków obowiązujących danego dnia (bez etatu — 1). Dla listy płac (009) i wymiaru.
CREATE OR REPLACE FUNCTION time_absence.working_hours(p_employment_id uuid, p_from date, p_to date)
    RETURNS numeric
    LANGUAGE sql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT coalesce(round(sum(d.hours * coalesce(t.fte_numerator::numeric / t.fte_denominator, 1)), 2), 0)
    FROM employment.employments m
    JOIN time_absence.calendar_days d
        ON d.company_id = m.company_id AND d.date BETWEEN p_from AND p_to AND d.kind = 'workday'
    LEFT JOIN employment.terms t
        ON t.employment_id = m.id AND daterange(t.valid_from, t.valid_to, '[]') @> d.date
    WHERE m.id = p_employment_id
      AND employment.period(m.start_date, m.end_date, m.terminated_at) @> d.date;
$$;

--changeset hr:time_absence.work_days runOnChange:true splitStatements:false
--comment: Liczba dni roboczych kalendarza spółki w okresie umowy (limity w dniach, R10).
CREATE OR REPLACE FUNCTION time_absence.work_days(p_employment_id uuid, p_from date, p_to date)
    RETURNS integer
    LANGUAGE sql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT count(*)::integer
    FROM employment.employments m
    JOIN time_absence.calendar_days d
        ON d.company_id = m.company_id AND d.date BETWEEN p_from AND p_to AND d.kind = 'workday' AND d.hours > 0
    WHERE m.id = p_employment_id
      AND employment.period(m.start_date, m.end_date, m.terminated_at) @> d.date;
$$;

--changeset hr:time_absence.calendar_missing runOnChange:true splitStatements:false
--comment: Czy w którymś roku okresu spółka nie ma kalendarza.
CREATE OR REPLACE FUNCTION time_absence.calendar_missing(p_company_id uuid, p_from date, p_to date)
    RETURNS boolean
    LANGUAGE sql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT EXISTS (
        SELECT FROM generate_series(extract(year FROM p_from)::integer, extract(year FROM p_to)::integer) y
        WHERE NOT EXISTS (
            SELECT FROM time_absence.calendar_days d
            WHERE d.company_id = p_company_id AND d.date BETWEEN make_date(y, 1, 1) AND make_date(y, 12, 31)
        )
    );
$$;

--changeset hr:time_absence.month_summary runOnChange:true splitStatements:false
--comment: Miesiące kalendarza spółki: dni robocze, godziny pełnego etatu, święta w sobotę i wyznaczone za nie dni wolne
--comment: (R3: brak wyznaczonego dnia = wymiar o 8 h za wysoki). Pusto, gdy rok nie ma kalendarza.
DROP FUNCTION IF EXISTS time_absence.month_summary(uuid, integer);
CREATE FUNCTION time_absence.month_summary(p_company_id uuid, p_year integer)
    RETURNS TABLE (
        month integer,
        work_days integer,
        hours numeric,
        saturday_holidays integer,
        designated_off integer
    )
    LANGUAGE sql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    WITH days AS (
        SELECT extract(month FROM d.date)::integer AS month,
               (count(*) FILTER (WHERE d.kind = 'workday' AND d.hours > 0))::integer AS work_days,
               coalesce(sum(d.hours) FILTER (WHERE d.kind = 'workday'), 0) AS hours,
               (count(*) FILTER (WHERE d.kind = 'designatedOff'))::integer AS designated_off
        FROM time_absence.calendar_days d
        WHERE d.company_id = p_company_id AND d.date BETWEEN make_date(p_year, 1, 1) AND make_date(p_year, 12, 31)
        GROUP BY 1
    )
    SELECT x.month, x.work_days, x.hours,
           (SELECT count(*)::integer FROM time_absence.holidays h
            WHERE extract(year FROM h.date) = p_year AND extract(month FROM h.date) = x.month
              AND extract(isodow FROM h.date) = 6),
           x.designated_off
    FROM days x
    ORDER BY x.month;
$$;

--changeset hr:time_absence.leave_proposal runOnChange:true splitStatements:false
--comment: Proponowany limit urlopu w godzinach (R13): wymiar 20 albo 26 dni × etat z pierwszego dnia umowy w roku,
--comment: proporcjonalnie 1/12 za każdy miesiąc kalendarzowy umowy w roku (niepełny miesiąc w górę, KP art. 155²ᵃ),
--comment: niepełny dzień w górę do pełnego (art. 154 § 2), 1 dzień = 8 h (art. 154²). Pierwsza praca:
--comment: w roku podjęcia 1/12 za każdy przepracowany pełny miesiąc (art. 153 § 1). Poza rokiem umowy — 0.
CREATE OR REPLACE FUNCTION time_absence.leave_proposal(
    p_employment_id uuid,
    p_year integer,
    p_base_days integer,
    p_first_job boolean
)
    RETURNS numeric
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_row employment.employments%ROWTYPE;
    v_period daterange;
    v_from date;
    v_to date;
    v_numerator integer;
    v_denominator integer;
    v_months integer;
    v_age interval;
BEGIN
    SELECT * INTO v_row FROM employment.employments m WHERE m.id = p_employment_id;
    v_period := employment.period(v_row.start_date, v_row.end_date, v_row.terminated_at)
        * daterange(make_date(p_year, 1, 1), make_date(p_year, 12, 31), '[]');
    IF v_period IS NULL OR isempty(v_period) THEN
        RETURN 0;
    END IF;
    v_from := lower(v_period);
    v_to := upper(v_period) - 1;

    SELECT coalesce(t.fte_numerator, 1), coalesce(t.fte_denominator, 1) INTO v_numerator, v_denominator
    FROM employment.terms t
    WHERE t.employment_id = p_employment_id AND daterange(t.valid_from, t.valid_to, '[]') @> v_from;
    v_numerator := coalesce(v_numerator, 1);
    v_denominator := coalesce(v_denominator, 1);

    IF p_first_job AND extract(year FROM v_row.start_date) = p_year THEN
        v_age := age(v_to + 1, v_row.start_date);
        v_months := least(12, (extract(year FROM v_age) * 12 + extract(month FROM v_age))::integer);
    ELSE
        v_months := extract(month FROM v_to)::integer - extract(month FROM v_from)::integer + 1;
    END IF;

    RETURN 8 * ceil((p_base_days * v_numerator * v_months)::numeric / (v_denominator * 12));
END;
$$;

--changeset hr:time_absence.leave_used runOnChange:true splitStatements:false
--comment: Godziny urlopu (UW, UZ) wykorzystane w roku, do dnia p_until włącznie; p_exclude_id — pomijana nieobecność
--comment: (sprawdzanie zmiany).
CREATE OR REPLACE FUNCTION time_absence.leave_used(
    p_employment_id uuid,
    p_year integer,
    p_exclude_id uuid DEFAULT NULL,
    p_until date DEFAULT NULL
)
    RETURNS numeric
    LANGUAGE sql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT coalesce(sum(time_absence.working_hours(a.employment_id, greatest(a.date_from, make_date(p_year, 1, 1)),
                                                   least(a.date_to, make_date(p_year, 12, 31), p_until))), 0)
    FROM time_absence.absences a
    JOIN time_absence.absence_types t ON t.code = a.absence_type_code
    WHERE a.employment_id = p_employment_id AND t.limit_kind = 'leave'
      AND a.id IS DISTINCT FROM p_exclude_id
      AND daterange(a.date_from, a.date_to, '[]') && daterange(make_date(p_year, 1, 1), make_date(p_year, 12, 31), '[]');
$$;

--changeset hr:time_absence.leave_entitled runOnChange:true splitStatements:false
--comment: Limit urlopu roku w godzinach: poprawiony przez Kadry albo propozycja z zapisanych ustawień; bez zapisanego
--comment: limitu — 0 (R13, R14).
CREATE OR REPLACE FUNCTION time_absence.leave_entitled(p_employment_id uuid, p_year integer)
    RETURNS numeric
    LANGUAGE sql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT coalesce((
        SELECT coalesce(e.entitled_hours,
                        time_absence.leave_proposal(e.employment_id, e.year, e.base_days, e.first_job))
        FROM time_absence.leave_entitlements e
        WHERE e.employment_id = p_employment_id AND e.year = p_year
    ), 0);
$$;

--changeset hr:time_absence.leave_carried runOnChange:true splitStatements:false
--comment: Urlop zaległy (R14): poprawiony przez Kadry albo niewykorzystany limit poprzedniego roku (gdy ten rok ma
--comment: zapisany limit), nie mniej niż 0.
CREATE OR REPLACE FUNCTION time_absence.leave_carried(p_employment_id uuid, p_year integer)
    RETURNS numeric
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_carried numeric;
BEGIN
    SELECT e.carried_over_hours INTO v_carried
    FROM time_absence.leave_entitlements e
    WHERE e.employment_id = p_employment_id AND e.year = p_year;
    IF v_carried IS NOT NULL THEN
        RETURN v_carried;
    END IF;
    IF NOT EXISTS (
        SELECT FROM time_absence.leave_entitlements e
        WHERE e.employment_id = p_employment_id AND e.year = p_year - 1
    ) THEN
        RETURN 0;
    END IF;
    RETURN greatest(0, time_absence.leave_entitled(p_employment_id, p_year - 1)
        + time_absence.leave_carried(p_employment_id, p_year - 1)
        - time_absence.leave_used(p_employment_id, p_year - 1));
END;
$$;

--changeset hr:time_absence.absence_validate runOnChange:true splitStatements:false
--comment: Błędy pól nieobecności (R7, R8). p_id = NULL przy nowej nieobecności. Limity sprawdza absence_limit_errors.
CREATE OR REPLACE FUNCTION time_absence.absence_validate(
    p_id uuid,
    p_employment_id uuid,
    p_type text,
    p_from date,
    p_to date,
    p_certificate text,
    p_note text
)
    RETURNS jsonb
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_errors jsonb := '{}';
    v_employment employment.employments%ROWTYPE;
    v_type time_absence.absence_types%ROWTYPE;
BEGIN
    SELECT * INTO v_employment FROM employment.employments m WHERE m.id = p_employment_id;
    SELECT * INTO v_type FROM time_absence.absence_types t WHERE t.code = p_type;

    IF p_type IS NULL THEN
        v_errors := v_errors || '{"absenceTypeId": ["required"]}';
    ELSIF v_type.code IS NULL THEN
        v_errors := v_errors || '{"absenceTypeId": ["notFound"]}';
    ELSIF v_employment.kind = 'specific_work'
        OR (v_employment.kind = 'mandate' AND (NOT v_type.sickness OR (p_from IS NOT NULL AND NOT EXISTS (
            SELECT FROM employment.terms t
            WHERE t.employment_id = p_employment_id AND daterange(t.valid_from, t.valid_to, '[]') @> p_from
              AND 'sickness' = ANY (t.insurances)
        )))) THEN
        -- Zlecenie: tylko choroba i tylko z dobrowolnym chorobowym; dzieło: żadna nieobecność (R8).
        v_errors := v_errors || '{"absenceTypeId": ["invalid"]}';
    END IF;

    IF p_from IS NULL THEN
        v_errors := v_errors || '{"dateFrom": ["required"]}';
    END IF;
    IF p_to IS NULL THEN
        v_errors := v_errors || '{"dateTo": ["required"]}';
    ELSIF p_to < p_from THEN
        v_errors := v_errors || '{"dateTo": ["beforeFrom"]}';
    END IF;
    IF p_from IS NOT NULL AND p_to >= p_from THEN
        IF NOT employment.period(v_employment.start_date, v_employment.end_date, v_employment.terminated_at)
            @> daterange(p_from, p_to, '[]') THEN
            v_errors := v_errors || '{"dateFrom": ["outsidePeriod"]}';
        ELSIF EXISTS (
            SELECT FROM time_absence.absences a
            WHERE a.employment_id = p_employment_id AND a.id IS DISTINCT FROM p_id
              AND daterange(a.date_from, a.date_to, '[]') && daterange(p_from, p_to, '[]')
        ) THEN
            v_errors := v_errors || '{"dateFrom": ["overlap"]}';
        END IF;
    END IF;

    IF v_type.sickness THEN
        IF p_certificate IS NULL THEN
            v_errors := v_errors || '{"certificateNumber": ["required"]}';
        ELSIF char_length(p_certificate) > 20 THEN
            v_errors := v_errors || '{"certificateNumber": ["maxLength"]}';
        END IF;
    ELSIF p_certificate IS NOT NULL AND v_type.code IS NOT NULL THEN
        v_errors := v_errors || '{"certificateNumber": ["invalid"]}';
    END IF;
    IF char_length(p_note) > 2000 THEN
        v_errors := v_errors || '{"note": ["maxLength"]}';
    END IF;
    RETURN v_errors;
END;
$$;

--changeset hr:time_absence.absence_limit_errors runOnChange:true splitStatements:false
--comment: Limity nieobecności (R10, R11) w każdym roku okresu: dni w roku (UZ, OD, SW, UOP), dni na zdarzenie (UO)
--comment: — dateTo limitExceeded z remainingDays; urlop (UW, UZ) ponad saldo — dateTo leaveLimitExceeded
--comment: z remainingHours. Dni = dni robocze kalendarza, godziny = godziny pracy × etat.
CREATE OR REPLACE FUNCTION time_absence.absence_limit_errors(
    p_id uuid,
    p_employment_id uuid,
    p_type text,
    p_from date,
    p_to date
)
    RETURNS jsonb
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_type time_absence.absence_types%ROWTYPE;
    v_year integer;
    v_lo date;
    v_hi date;
    v_used_days integer;
    v_days integer;
    v_remaining numeric;
BEGIN
    SELECT * INTO v_type FROM time_absence.absence_types t WHERE t.code = p_type;
    IF v_type.limit_kind = 'event' THEN
        v_days := time_absence.work_days(p_employment_id, p_from, p_to);
        IF v_days > v_type.limit_days THEN
            RETURN jsonb_build_object('dateTo', jsonb_build_array('limitExceeded'),
                'remainingDays', jsonb_build_array(v_type.limit_days::text));
        END IF;
        RETURN '{}';
    END IF;

    FOR v_year IN extract(year FROM p_from)::integer .. extract(year FROM p_to)::integer LOOP
        v_lo := greatest(p_from, make_date(v_year, 1, 1));
        v_hi := least(p_to, make_date(v_year, 12, 31));
        IF v_type.limit_days IS NOT NULL AND v_type.limit_kind IN ('leave', 'yearDays') THEN
            SELECT coalesce(sum(time_absence.work_days(a.employment_id, greatest(a.date_from, make_date(v_year, 1, 1)),
                                                       least(a.date_to, make_date(v_year, 12, 31)))), 0)
            INTO v_used_days
            FROM time_absence.absences a
            WHERE a.employment_id = p_employment_id AND a.absence_type_code = p_type
              AND a.id IS DISTINCT FROM p_id
              AND daterange(a.date_from, a.date_to, '[]')
                  && daterange(make_date(v_year, 1, 1), make_date(v_year, 12, 31), '[]');
            IF v_used_days + time_absence.work_days(p_employment_id, v_lo, v_hi) > v_type.limit_days THEN
                RETURN jsonb_build_object('dateTo', jsonb_build_array('limitExceeded'),
                    'remainingDays', jsonb_build_array(greatest(v_type.limit_days - v_used_days, 0)::text));
            END IF;
        END IF;
        IF v_type.limit_kind = 'leave' THEN
            v_remaining := time_absence.leave_entitled(p_employment_id, v_year)
                + time_absence.leave_carried(p_employment_id, v_year)
                - time_absence.leave_used(p_employment_id, v_year, p_id);
            IF time_absence.working_hours(p_employment_id, v_lo, v_hi) > v_remaining THEN
                RETURN jsonb_build_object('dateTo', jsonb_build_array('leaveLimitExceeded'),
                    'remainingHours', jsonb_build_array(greatest(v_remaining, 0)::text));
            END IF;
        END IF;
    END LOOP;
    RETURN '{}';
END;
$$;

--changeset hr:time_absence.statutory_holidays runOnChange:true splitStatements:false
--comment: Święta ustawowe roku wyliczone z ustawy o dniach wolnych od pracy (art. 1 pkt 1, t.j. Dz.U. 2025 poz. 296;
--comment: Wigilia od 1.02.2025 — Dz.U. 2024 poz. 1965): 10 dat stałych i 4 dni od Wielkanocy (niedziela Wielkanocy,
--comment: +1 poniedziałek, +49 Zielone Świątki, +60 Boże Ciało). Wielkanoc — algorytm Meeusa/Jonesa/Butchera dla
--comment: kalendarza gregoriańskiego. Obecna lista obowiązuje od 2025 r.; lata wcześniejsze — U0003
--comment: statutoryHolidaysUnsupported (wpis ręczny).
CREATE OR REPLACE FUNCTION time_absence.statutory_holidays(p_year integer)
    RETURNS TABLE (date date, name text)
    LANGUAGE plpgsql
    IMMUTABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    a integer := p_year % 19;
    b integer := p_year / 100;
    c integer := p_year % 100;
    h integer;
    l integer;
    m integer;
    v_easter date;
BEGIN
    IF p_year IS NULL OR p_year < 2025 THEN
        RAISE EXCEPTION 'statutory holidays known from 2025' USING ERRCODE = 'U0003',
            HINT = 'statutoryHolidaysUnsupported';
    END IF;
    h := (19 * a + b - b / 4 - (b - (b + 8) / 25 + 1) / 3 + 15) % 30;
    l := (32 + 2 * (b % 4) + 2 * (c / 4) - h - c % 4) % 7;
    m := (a + 11 * h + 22 * l) / 451;
    v_easter := make_date(p_year, (h + l - 7 * m + 114) / 31, (h + l - 7 * m + 114) % 31 + 1);

    RETURN QUERY
    SELECT s.date, s.name
    FROM (
        SELECT make_date(p_year, v.month, v.day) AS date, v.name
        FROM (VALUES
            (1, 1, 'Nowy Rok'),
            (1, 6, 'Święto Trzech Króli'),
            (5, 1, 'Święto Państwowe'),
            (5, 3, 'Święto Narodowe Trzeciego Maja'),
            (8, 15, 'Wniebowzięcie Najświętszej Maryi Panny'),
            (11, 1, 'Wszystkich Świętych'),
            (11, 11, 'Narodowe Święto Niepodległości'),
            (12, 24, 'Wigilia Bożego Narodzenia'),
            (12, 25, 'Pierwszy dzień Bożego Narodzenia'),
            (12, 26, 'Drugi dzień Bożego Narodzenia')
        ) AS v (month, day, name)
        UNION ALL
        SELECT v_easter + v.offset_days, v.name
        FROM (VALUES
            (0, 'Pierwszy dzień Wielkiej Nocy'),
            (1, 'Drugi dzień Wielkiej Nocy'),
            (49, 'Pierwszy dzień Zielonych Świątek'),
            (60, 'Boże Ciało')
        ) AS v (offset_days, name)
    ) AS s
    ORDER BY s.date;
END;
$$;
