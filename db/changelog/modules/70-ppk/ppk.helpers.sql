--liquibase formatted sql

--changeset hr:ppk.require_employee_access runOnChange:true splitStatements:false
--comment: Kadry i Administrator — każda kartoteka; Pracownik — tylko powiązana z kontem (R14, R15).
CREATE OR REPLACE FUNCTION ppk.require_employee_access(p_employee_id uuid)
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

--changeset hr:ppk.age_on runOnChange:true splitStatements:false
--comment: Wiek w pełnych latach w dniu (R4).
CREATE OR REPLACE FUNCTION ppk.age_on(p_birth_date date, p_on date)
    RETURNS integer
    LANGUAGE sql
    IMMUTABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT extract(year FROM age(p_on, p_birth_date))::integer;
$$;

--changeset hr:ppk.employed_days runOnChange:true splitStatements:false
--comment: Dni w okresie, w których kartoteka jest osobą zatrudnioną (R2; ustawa o PPK art. 2 ust. 1 pkt 18): umowa
--comment: o pracę od 18. roku życia (młodociany — KP art. 190 — nie), zlecenie z ubezpieczeniem emerytalnym i rentowym
--comment: w warunkach z dnia. Dzieło — nie.
CREATE OR REPLACE FUNCTION ppk.employed_days(p_employee_id uuid, p_from date, p_to date)
    RETURNS TABLE (day date)
    LANGUAGE sql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT DISTINCT d::date
    FROM employment.employments m
    JOIN employees.employees e ON e.id = m.employee_id
    CROSS JOIN LATERAL generate_series(greatest(m.start_date, p_from),
                                       least(coalesce(m.terminated_at, m.end_date, p_to), p_to),
                                       interval '1 day') d
    WHERE m.employee_id = p_employee_id
      AND (
          (m.kind = 'employment' AND d::date >= (e.birth_date + interval '18 years')::date)
          OR (m.kind = 'mandate' AND EXISTS (
              SELECT FROM employment.terms t
              WHERE t.employment_id = m.id AND daterange(t.valid_from, t.valid_to, '[]') @> d::date
                AND t.insurances @> ARRAY['pension', 'disability']
          ))
      );
$$;

--changeset hr:ppk.is_employed_person runOnChange:true splitStatements:false
--comment: Czy kartoteka jest osobą zatrudnioną w dniu (R2).
CREATE OR REPLACE FUNCTION ppk.is_employed_person(p_employee_id uuid, p_on date)
    RETURNS boolean
    LANGUAGE sql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT EXISTS (SELECT FROM ppk.employed_days(p_employee_id, p_on, p_on));
$$;

--changeset hr:ppk.three_months_reached_on runOnChange:true splitStatements:false
--comment: Dzień, w którym upłynęły 3 miesiące zatrudnienia (R3; art. 16 ust. 1–2): pierwszy dzień zatrudnienia X,
--comment: w którym dni zatrudnienia w tej spółce z 12 miesięcy wstecz (X − 1 rok, X] — różne tytuły sumowane — jest
--comment: nie mniej niż dni okresu 3 miesięcy kończącego się w X, [X + 1 − 3 miesiące, X] (od 1.03 do 31.05). Liczy się bieżący okres: po ostatnim dniu, w którym warunek
--comment: nie był spełniony (do p_on). Patrzy do p_on + 92 dni naprzód (data przyszła = „jeszcze nie”); NULL — w tym
--comment: horyzoncie 3 miesiące nie upłyną. Zatrudnienie od 2026-01-15 → 2026-04-14.
CREATE OR REPLACE FUNCTION ppk.three_months_reached_on(p_employee_id uuid, p_on date)
    RETURNS date
    LANGUAGE sql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    WITH covered AS (
        SELECT x.day
        FROM ppk.employed_days(p_employee_id,
                               (SELECT min(m.start_date) FROM employment.employments m
                                WHERE m.employee_id = p_employee_id),
                               p_on + 92) x
    ), counted AS (
        SELECT c.day,
               count(*) OVER (ORDER BY c.day RANGE BETWEEN interval '1 year -1 day' PRECEDING AND CURRENT ROW) AS n
        FROM covered c
    ), checked AS (
        SELECT c.day, c.n >= (c.day + 1) - ((c.day + 1) - interval '3 months')::date AS ok
        FROM counted c
    )
    SELECT min(c.day)
    FROM checked c
    WHERE c.ok
      AND c.day > coalesce((SELECT max(x.day) FROM checked x WHERE NOT x.ok AND x.day <= p_on), '-infinity'::date);
$$;

--changeset hr:ppk.enrollment_deadline runOnChange:true splitStatements:false
--comment: Termin zawarcia umowy o prowadzenie PPK (R3; art. 16 ust. 1): 10. dzień miesiąca następującego po miesiącu,
--comment: w którym upłynęły 3 miesiące zatrudnienia.
CREATE OR REPLACE FUNCTION ppk.enrollment_deadline(p_reached_on date)
    RETURNS date
    LANGUAGE sql
    IMMUTABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT (date_trunc('month', p_reached_on) + interval '1 month 9 days')::date;
$$;

--changeset hr:ppk.effective_from runOnChange:true splitStatements:false
--comment: Od kiedy działa deklaracja (R9): rezygnacja — od miesiąca złożenia (art. 23 ust. 4), wniosek o zapis — od
--comment: dnia złożenia, wniosek o wznowienie i stawki — od miesiąca następującego (art. 23 ust. 11, art. 27 ust. 4).
CREATE OR REPLACE FUNCTION ppk.effective_from(p_kind text, p_submitted_at date)
    RETURNS date
    LANGUAGE sql
    IMMUTABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT CASE p_kind
        WHEN 'resignation' THEN date_trunc('month', p_submitted_at)::date
        WHEN 'enrollmentRequest' THEN p_submitted_at
        ELSE (date_trunc('month', p_submitted_at) + interval '1 month')::date
    END;
$$;

--changeset hr:ppk.last_state_kind runOnChange:true splitStatements:false
--comment: Ostatnia deklaracja stanu wpłat (rezygnacja, wniosek o wznowienie, wznowienie z cyklu) działająca w dniu
--comment: p_on (pusty — dowolna); przy tym samym dniu działania wygrywa później złożona.
CREATE OR REPLACE FUNCTION ppk.last_state_kind(p_employee_id uuid, p_on date)
    RETURNS text
    LANGUAGE sql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT d.kind
    FROM ppk.declarations d
    WHERE d.employee_id = p_employee_id AND d.kind IN ('resignation', 'restartRequest', 'autoRestart')
      AND (p_on IS NULL OR d.effective_from <= p_on)
    ORDER BY d.effective_from DESC, d.submitted_at DESC, d.created_at DESC
    LIMIT 1;
$$;

--changeset hr:ppk.status_on runOnChange:true splitStatements:false
--comment: Status uczestnika w dniu (R11): resigned (od miesiąca rezygnacji do miesiąca przed wznowieniem), active
--comment: (uczestnictwo od enrolled_at), notEnrolled.
CREATE OR REPLACE FUNCTION ppk.status_on(p_employee_id uuid, p_on date)
    RETURNS text
    LANGUAGE sql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT CASE
        WHEN ppk.last_state_kind(p_employee_id, p_on) = 'resignation' THEN 'resigned'
        WHEN EXISTS (
            SELECT FROM ppk.participations p WHERE p.employee_id = p_employee_id AND p.enrolled_at <= p_on
        ) THEN 'active'
        ELSE 'notEnrolled'
    END;
$$;

--changeset hr:ppk.rates_on runOnChange:true splitStatements:false
--comment: Stawki PPK w procentach obowiązujące w dniu — dla listy płac (009, R8, R11): pracownika z okresu stawek,
--comment: pracodawcy podstawowa z parametru ppk.pracodawca.podstawowa (brak = U0006) i dodatkowa z ustawień spółki
--comment: (brak = 0). Status inny niż active — same zera.
DROP FUNCTION IF EXISTS ppk.rates_on(uuid, date);
CREATE FUNCTION ppk.rates_on(p_employee_id uuid, p_on date)
    RETURNS TABLE (
        employee_basic_rate numeric,
        employee_additional_rate numeric,
        employer_basic_rate numeric,
        employer_additional_rate numeric
    )
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    IF ppk.status_on(p_employee_id, p_on) <> 'active' THEN
        RETURN QUERY SELECT 0::numeric, 0::numeric, 0::numeric, 0::numeric;
        RETURN;
    END IF;
    RETURN QUERY
    SELECT coalesce(r.employee_basic_rate, 0)::numeric,
           coalesce(r.employee_additional_rate, 0)::numeric,
           round(platform.legal_parameter('ppk.pracodawca.podstawowa', p_on) * 100, 2),
           coalesce((
               SELECT s.employer_additional_rate
               FROM ppk.company_settings s
               WHERE s.company_id = e.company_id AND daterange(s.valid_from, s.valid_to, '[]') @> p_on
           ), 0)::numeric
    FROM employees.employees e
    JOIN ppk.participations p ON p.employee_id = e.id
    LEFT JOIN ppk.rate_periods r ON r.participation_id = p.id AND daterange(r.valid_from, r.valid_to, '[]') @> p_on
    WHERE e.id = p_employee_id;
END;
$$;

--changeset hr:ppk.rebuild_rates runOnChange:true splitStatements:false
--comment: Odbudowa okresów stawek uczestnictwa (R6, R10): od enrolled_at stawki początkowe, potem każda deklaracja
--comment: obniżenia, wpłaty dodatkowej, wznowienia (z ostatnimi stawkami) otwiera okres od swojego effective_from
--comment: i zamyka poprzedni dzień wcześniej; deklaracje z tym samym dniem tworzą jeden okres.
CREATE OR REPLACE FUNCTION ppk.rebuild_rates(p_employee_id uuid)
    RETURNS void
    LANGUAGE plpgsql
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_participation ppk.participations%ROWTYPE;
    v_basic numeric;
    v_additional numeric;
    v_start date;
    v_event record;
BEGIN
    SELECT * INTO v_participation FROM ppk.participations p WHERE p.employee_id = p_employee_id;
    IF NOT FOUND THEN
        RETURN;
    END IF;
    DELETE FROM ppk.rate_periods r WHERE r.participation_id = v_participation.id;
    v_basic := v_participation.initial_basic_rate;
    v_additional := v_participation.initial_additional_rate;
    v_start := v_participation.enrolled_at;
    FOR v_event IN
        SELECT greatest(d.effective_from, v_participation.enrolled_at) AS effective_from, d.kind, d.rate
        FROM ppk.declarations d
        WHERE d.employee_id = p_employee_id
          AND d.kind IN ('reducedRate', 'additionalRate', 'restartRequest', 'autoRestart')
        ORDER BY 1, d.submitted_at, d.created_at
    LOOP
        IF v_event.effective_from > v_start THEN
            INSERT INTO ppk.rate_periods (participation_id, valid_from, valid_to, employee_basic_rate,
                                          employee_additional_rate)
            VALUES (v_participation.id, v_start, v_event.effective_from - 1, v_basic, v_additional);
            v_start := v_event.effective_from;
        END IF;
        IF v_event.kind = 'reducedRate' THEN
            v_basic := v_event.rate;
        ELSIF v_event.kind = 'additionalRate' THEN
            v_additional := v_event.rate;
        END IF;
    END LOOP;
    INSERT INTO ppk.rate_periods (participation_id, valid_from, valid_to, employee_basic_rate, employee_additional_rate)
    VALUES (v_participation.id, v_start, NULL, v_basic, v_additional);
END;
$$;

--changeset hr:ppk.cycle_get runOnChange:true splitStatements:false
--comment: Daty cyklu ponownego zapisu (R12) z parametrów ppk.ponowny.zapis.informacja i ppk.ponowny.zapis.wznowienie
--comment: (wartość RRRRMMDD, okres = rok cyklu). Rok bez obu dat w tym roku — U0001 {"year": ["notCycleYear"]}.
CREATE OR REPLACE FUNCTION ppk.cycle_get(
    p_year integer,
    OUT information_deadline date,
    OUT restart_date date
)
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_information numeric;
    v_restart numeric;
BEGIN
    IF p_year BETWEEN 2019 AND 2100 THEN
        v_information := platform.legal_parameter_or_null('ppk.ponowny.zapis.informacja', make_date(p_year, 1, 1));
        v_restart := platform.legal_parameter_or_null('ppk.ponowny.zapis.wznowienie', make_date(p_year, 1, 1));
    END IF;
    IF v_information IS NOT NULL AND v_restart IS NOT NULL THEN
        information_deadline := to_date(v_information::bigint::text, 'YYYYMMDD');
        restart_date := to_date(v_restart::bigint::text, 'YYYYMMDD');
    END IF;
    IF information_deadline IS NULL OR extract(year FROM information_deadline) <> p_year
        OR extract(year FROM restart_date) <> p_year THEN
        PERFORM platform.fail_validation('{"year": ["notCycleYear"]}');
    END IF;
END;
$$;

--changeset hr:ppk.reenrollment_rows runOnChange:true splitStatements:false
--comment: Lista ponownego zapisu spółki w roku cyklu (R13): osoby, których ostatnia deklaracja stanu przed dniem
--comment: wznowienia to rezygnacja, zatrudnione w dniu informacji (albo już wznowione w tym cyklu). Grupa z wieku
--comment: w ostatnim dniu na informację: restart (do 55 lat — wznowienie od 1.04), requestOnly (55–69 — tylko na
--comment: wniosek, art. 23 ust. 9), noRestart (70+, ust. 8). resigned_after_information — rezygnacja złożona
--comment: w dniu informacji lub później: bez wznowienia.
DROP FUNCTION IF EXISTS ppk.reenrollment_rows(uuid, integer);
CREATE FUNCTION ppk.reenrollment_rows(p_company_id uuid, p_year integer)
    RETURNS TABLE (
        employee_id uuid,
        employee_number text,
        first_name text,
        last_name text,
        birth_date date,
        age integer,
        resigned_from date,
        group_kind text,
        resigned_after_information boolean,
        restarted boolean
    )
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_cycle record;
    v_informed_at date;
BEGIN
    SELECT * INTO v_cycle FROM ppk.cycle_get(p_year);
    SELECT c.informed_at INTO v_informed_at
    FROM ppk.reenrollment_cycles c WHERE c.company_id = p_company_id AND c.year = p_year;

    RETURN QUERY
    SELECT x.id, x.number, x.first_name, x.last_name, x.birth_date, x.age, x.resigned_from,
           CASE WHEN x.age < 55 THEN 'restart' WHEN x.age < 70 THEN 'requestOnly' ELSE 'noRestart' END,
           v_informed_at IS NOT NULL AND EXISTS (
               SELECT FROM ppk.declarations d
               WHERE d.employee_id = x.id AND d.kind = 'resignation' AND d.submitted_at >= v_informed_at
           ),
           x.restarted
    FROM (
        SELECT e.id, e.number, e.first_name, e.last_name, e.birth_date,
               ppk.age_on(e.birth_date, v_cycle.information_deadline) AS age,
               (SELECT d.effective_from FROM ppk.declarations d
                WHERE d.employee_id = e.id AND d.kind IN ('resignation', 'restartRequest', 'autoRestart')
                  AND d.effective_from < v_cycle.restart_date
                ORDER BY d.effective_from DESC, d.submitted_at DESC, d.created_at DESC LIMIT 1) AS resigned_from,
               ppk.last_state_kind(e.id, v_cycle.restart_date - 1) AS last_kind,
               EXISTS (SELECT FROM ppk.declarations d
                       WHERE d.employee_id = e.id AND d.kind = 'autoRestart' AND d.cycle_year = p_year) AS restarted
        FROM employees.employees e
        WHERE e.company_id = p_company_id
    ) x
    WHERE x.last_kind = 'resignation'
      AND (x.restarted OR ppk.is_employed_person(x.id, v_cycle.information_deadline))
    ORDER BY lower(x.last_name), lower(x.first_name), x.number;
END;
$$;
