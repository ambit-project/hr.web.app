--liquibase formatted sql

--changeset hr:payroll.params runOnChange:true splitStatements:false
--comment: Ślad parametrów prawnych pozycji (R24): {"kod": {"value": …, "validFrom": …}} z wartości obowiązujących w dniu.
CREATE OR REPLACE FUNCTION payroll.params(p_codes text[], p_on date)
    RETURNS jsonb
    LANGUAGE sql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT coalesce(jsonb_object_agg(p.code, jsonb_build_object('value', p.value, 'validFrom', p.valid_from)), '{}')
    FROM platform.legal_parameters p
    WHERE p.code = ANY (p_codes) AND daterange(p.valid_from, p.valid_to, '[]') @> p_on;
$$;

--changeset hr:payroll.line_constructors runOnChange:true splitStatements:false
--comment: Pozycje silnika: przychód z cechami (ZUS, zdrowotna, PIT, chorobowe), pozycja innej strony, ostrzeżenie.
CREATE OR REPLACE FUNCTION payroll.income(
    p_code text,
    p_base numeric,
    p_rate numeric,
    p_amount numeric,
    p_type_id uuid,
    p_zus boolean,
    p_health boolean,
    p_taxable boolean,
    p_sickness boolean,
    p_trace jsonb
)
    RETURNS payroll.line_t
    LANGUAGE sql
    IMMUTABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT ROW(p_code, 'income', p_base, p_rate, p_amount, p_type_id, p_zus, p_health, p_taxable, p_sickness,
               coalesce(p_trace, '{}'))::payroll.line_t;
$$;

CREATE OR REPLACE FUNCTION payroll.item(
    p_code text,
    p_side text,
    p_base numeric,
    p_rate numeric,
    p_amount numeric,
    p_trace jsonb,
    p_type_id uuid DEFAULT NULL
)
    RETURNS payroll.line_t
    LANGUAGE sql
    IMMUTABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT ROW(p_code, p_side, p_base, p_rate, p_amount, p_type_id, NULL, NULL, NULL, NULL,
               coalesce(p_trace, '{}'))::payroll.line_t;
$$;

CREATE OR REPLACE FUNCTION payroll.warning(p_code text, p_details jsonb DEFAULT '{}')
    RETURNS payroll.line_t
    LANGUAGE sql
    IMMUTABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT payroll.item(p_code, 'warning', NULL, NULL, 0, p_details);
$$;

--changeset hr:payroll.system_income runOnChange:true splitStatements:false
--comment: Przychód ze składnika systemowego (006: ZASAD, CHOR, NADG50, NADG100, ZLEC, DZIELO) — cechy z jego definicji.
--comment: p_type_code — składnik, z którego brać cechy (np. ZASAD dla pomniejszeń ZASAD-CH, ZASAD-NB).
CREATE OR REPLACE FUNCTION payroll.system_income(
    p_code text,
    p_type_code text,
    p_base numeric,
    p_rate numeric,
    p_amount numeric,
    p_trace jsonb
)
    RETURNS payroll.line_t
    LANGUAGE sql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT payroll.income(p_code, p_base, p_rate, p_amount, t.id, t.in_zus_base, t.in_health_base, t.taxable,
                          t.in_sickness_base, p_trace)
    FROM pay_components.component_types t
    WHERE t.code = p_type_code AND t.is_system;
$$;

--changeset hr:payroll.sum_lines runOnChange:true splitStatements:false
--comment: Suma kwot pozycji o podanych kodach (pusta tablica kodów = wszystkie) i stronie (NULL = każda).
CREATE OR REPLACE FUNCTION payroll.sum_lines(p_lines payroll.line_t[], p_codes text[], p_side text DEFAULT NULL)
    RETURNS numeric
    LANGUAGE sql
    IMMUTABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT coalesce(sum(l.amount), 0)
    FROM unnest(p_lines) l
    WHERE (cardinality(p_codes) = 0 OR l.code = ANY (p_codes)) AND (p_side IS NULL OR l.side = p_side);
$$;

--changeset hr:payroll.contract_kind runOnChange:true splitStatements:false
--comment: Rodzaj umowy (004) dla rodzaju listy (R1).
CREATE OR REPLACE FUNCTION payroll.contract_kind(p_payroll_kind text)
    RETURNS text
    LANGUAGE sql
    IMMUTABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT CASE p_payroll_kind
        WHEN 'employment' THEN 'employment'
        WHEN 'mandate' THEN 'mandate'
        WHEN 'specificWork' THEN 'specific_work'
    END;
$$;

--changeset hr:payroll.calendar_hours runOnChange:true splitStatements:false
--comment: Godziny pełnego etatu w kalendarzu spółki w okresie (wymiar; R5, R8, R9).
CREATE OR REPLACE FUNCTION payroll.calendar_hours(p_company_id uuid, p_from date, p_to date)
    RETURNS numeric
    LANGUAGE sql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT coalesce(sum(d.hours), 0)
    FROM time_absence.calendar_days d
    WHERE d.company_id = p_company_id AND d.date BETWEEN p_from AND p_to AND d.kind = 'workday';
$$;

--changeset hr:payroll.settings_on runOnChange:true splitStatements:false
--comment: Ustawienia płacowe spółki w dniu (R6, R13): stopa wypadkowa i wypłata zasiłków przez płatnika. Bez okresu
--comment: ustawień — stopa z parametru zus.wypadkowa.domyslna (brak = U0006) i zasiłki wypłaca ZUS.
CREATE OR REPLACE FUNCTION payroll.settings_on(
    p_company_id uuid,
    p_on date,
    OUT settings_id uuid,
    OUT accident_rate numeric,
    OUT pays_sickness_allowance boolean
)
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    SELECT s.id, s.accident_rate, s.pays_sickness_allowance INTO settings_id, accident_rate, pays_sickness_allowance
    FROM payroll.company_settings s
    WHERE s.company_id = p_company_id AND daterange(s.valid_from, s.valid_to, '[]') @> p_on;
    IF settings_id IS NULL THEN
        accident_rate := platform.legal_parameter('zus.wypadkowa.domyslna', p_on);
        pays_sickness_allowance := false;
    END IF;
END;
$$;

--changeset hr:payroll.eligible_employments runOnChange:true splitStatements:false
--comment: Umowy rodzaju listy w spółce do składu (R3): umowa o pracę i zlecenie trwające choć jeden dzień w okresie,
--comment: zlecenie także z potwierdzonymi godzinami kończącymi się w okresie (007 R17); dzieło — kończące się w okresie
--comment: (R12, wypłata w miesiącu końca umowy [D]).
CREATE OR REPLACE FUNCTION payroll.eligible_employments(p_company_id uuid, p_kind text, p_from date, p_to date)
    RETURNS SETOF uuid
    LANGUAGE sql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT m.id
    FROM employment.employments m
    WHERE m.company_id = p_company_id AND m.kind = payroll.contract_kind(p_kind)
      AND (
          (m.kind IN ('employment', 'mandate')
              AND employment.period(m.start_date, m.end_date, m.terminated_at) && daterange(p_from, p_to, '[]'))
          OR (m.kind = 'mandate' AND EXISTS (
              SELECT FROM time_absence.mandate_hours h
              WHERE h.employment_id = m.id AND h.confirmed_at IS NOT NULL AND h.date_to BETWEEN p_from AND p_to
          ))
          OR (m.kind = 'specific_work' AND coalesce(m.terminated_at, m.end_date) BETWEEN p_from AND p_to)
      );
$$;

--changeset hr:payroll.closed_lines runOnChange:true splitStatements:false
--comment: Pozycje zamkniętych list kartoteki z danymi listy — źródło sum rocznych, podstaw chorobowego i średnich (R25).
CREATE OR REPLACE FUNCTION payroll.closed_lines(p_employee_id uuid)
    RETURNS TABLE (
        payroll_id uuid,
        kind text,
        corrects_payroll_id uuid,
        period_from date,
        payment_date date,
        entry_id uuid,
        employment_id uuid,
        contract_kind text,
        code text,
        base numeric,
        amount numeric,
        trace jsonb
    )
    LANGUAGE sql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT p.id, p.kind, p.corrects_payroll_id, p.period_from, p.payment_date, e.id, e.employment_id, m.kind, l.code,
           l.base, l.amount, l.trace
    FROM payroll.entries e
    JOIN payroll.payrolls p ON p.id = e.payroll_id AND p.status = 'closed'
    JOIN employment.employments m ON m.id = e.employment_id
    JOIN payroll.lines l ON l.entry_id = e.id
    WHERE e.employee_id = p_employee_id;
$$;

--changeset hr:payroll.year_totals runOnChange:true splitStatements:false
--comment: Sumy roczne kartoteki z zamkniętych list (R25, propozycja AnnualAccumulator bez tabeli): podstawa
--comment: emerytalno-rentowa po okresie listy (limit 30-krotności, R13), podstawa PIT umów o pracę i wszystkich umów
--comment: po dacie wypłaty (próg 120 000 zł R16, art. 31c), przychód zwolniony (R17), koszty 50% (R18), koszty
--comment: pracownicze. p_zus_exclude / p_pit_exclude — listy pomijane (naliczana lista i jej rodzina przy korekcie);
--comment: p_period_until — podstawa ZUS tylko z okresów do tego dnia (korekta liczy limit na dzień swojego okresu).
CREATE OR REPLACE FUNCTION payroll.year_totals(
    p_employee_id uuid,
    p_year integer,
    p_zus_exclude uuid[] DEFAULT '{}',
    p_pit_exclude uuid[] DEFAULT '{}',
    p_period_until date DEFAULT 'infinity',
    OUT pension_base numeric,
    OUT tax_base_employment numeric,
    OUT tax_base_all numeric,
    OUT exempt_income numeric,
    OUT author_costs numeric,
    OUT employee_costs numeric
)
    LANGUAGE sql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT
        coalesce(sum(c.base) FILTER (WHERE c.code = 'ZUS-E-P' AND extract(year FROM c.period_from) = p_year
                                     AND c.period_from <= p_period_until
                                     AND NOT c.payroll_id = ANY (p_zus_exclude)), 0),
        coalesce(sum(c.amount) FILTER (WHERE c.code = 'PODST-PIT' AND c.contract_kind = 'employment'
                                       AND extract(year FROM c.payment_date) = p_year
                                       AND NOT c.payroll_id = ANY (p_pit_exclude)), 0),
        coalesce(sum(c.amount) FILTER (WHERE c.code = 'PODST-PIT' AND extract(year FROM c.payment_date) = p_year
                                       AND NOT c.payroll_id = ANY (p_pit_exclude)), 0),
        coalesce(sum(c.amount) FILTER (WHERE c.code = 'ZWOL-MLODZI' AND extract(year FROM c.payment_date) = p_year
                                       AND NOT c.payroll_id = ANY (p_pit_exclude)), 0),
        coalesce(sum(c.amount) FILTER (WHERE c.code = 'KUP50' AND extract(year FROM c.payment_date) = p_year
                                       AND NOT c.payroll_id = ANY (p_pit_exclude)), 0),
        coalesce(sum(c.amount) FILTER (WHERE c.code = 'KUP' AND c.contract_kind = 'employment'
                                       AND extract(year FROM c.payment_date) = p_year
                                       AND NOT c.payroll_id = ANY (p_pit_exclude)), 0)
    FROM payroll.closed_lines(p_employee_id) c;
$$;

--changeset hr:payroll.garnishment_collected runOnChange:true splitStatements:false
--comment: Kwota ściągnięta z zajęcia — suma pozycji ALIM i KOMOR zamkniętych list ze śladem zajęcia (R25; 006 R21).
CREATE OR REPLACE FUNCTION payroll.garnishment_collected(p_garnishment_id uuid)
    RETURNS numeric
    LANGUAGE sql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT coalesce(sum(l.amount), 0)
    FROM payroll.lines l
    JOIN payroll.entries e ON e.id = l.entry_id
    JOIN payroll.payrolls p ON p.id = e.payroll_id AND p.status = 'closed'
    WHERE l.code IN ('ALIM', 'KOMOR') AND l.trace ->> 'garnishmentId' = p_garnishment_id::text;
$$;

--changeset hr:payroll.age_on runOnChange:true splitStatements:false
--comment: Wiek w pełnych latach w dniu.
CREATE OR REPLACE FUNCTION payroll.age_on(p_birth_date date, p_on date)
    RETURNS integer
    LANGUAGE sql
    IMMUTABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT extract(year FROM age(p_on, p_birth_date))::integer;
$$;

--changeset hr:payroll.is_male runOnChange:true splitStatements:false
--comment: Płeć z PESEL (10. cyfra nieparzysta — mężczyzna); NULL, gdy kartoteka nie ma PESEL (R14 [D]).
CREATE OR REPLACE FUNCTION payroll.is_male(p_employee_id uuid)
    RETURNS boolean
    LANGUAGE sql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT substr(employees.decrypt(e.pesel_enc), 10, 1)::integer % 2 = 1
    FROM employees.employees e
    WHERE e.id = p_employee_id AND e.pesel_enc IS NOT NULL;
$$;

--changeset hr:payroll.guard runOnChange:true splitStatements:false
--comment: Blokada zmian danych źródłowych w okresie zamkniętej listy (R25) i oznaczenie list naliczonych jako
--comment: nieaktualne (R2). Umowa (p_employment_id) albo cała kartoteka (p_employment_id NULL); okres [p_from, p_to]
--comment: (p_to NULL = bez końca). p_by_payment — oświadczenia podatkowe: liczy się miesiąc daty wypłaty listy.
--comment: Zamknięta lista nie blokuje, gdy ma otwartą albo naliczoną listę korygującą z tą umową (R4 [D]: zmiana
--comment: danych za zamknięty okres tylko przez korektę). U0003 payrollClosed.
CREATE OR REPLACE FUNCTION payroll.guard(
    p_employee_id uuid,
    p_employment_id uuid,
    p_from date,
    p_to date,
    p_by_payment boolean DEFAULT false
)
    RETURNS void
    LANGUAGE plpgsql
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_range daterange := daterange(coalesce(p_from, '-infinity'), coalesce(p_to, 'infinity'), '[]');
BEGIN
    IF EXISTS (
        SELECT FROM payroll.payrolls p
        JOIN payroll.entries e ON e.payroll_id = p.id
        WHERE p.status = 'closed'
          AND (e.employment_id = p_employment_id OR (p_employment_id IS NULL AND e.employee_id = p_employee_id))
          AND CASE WHEN p_by_payment THEN v_range @> p.payment_date
                   ELSE v_range && daterange(p.period_from, p.period_to, '[]') END
          AND NOT EXISTS (
              SELECT FROM payroll.payrolls c
              JOIN payroll.members cm ON cm.payroll_id = c.id AND cm.employment_id = e.employment_id AND NOT cm.excluded
              WHERE c.corrects_payroll_id = coalesce(p.corrects_payroll_id, p.id) AND c.status <> 'closed'
          )
    ) THEN
        RAISE EXCEPTION 'data is used on a closed payroll' USING ERRCODE = 'U0003', HINT = 'payrollClosed';
    END IF;

    UPDATE payroll.payrolls p
    SET is_stale = true
    WHERE p.status = 'calculated' AND NOT p.is_stale
      AND CASE WHEN p_by_payment THEN v_range @> p.payment_date
               ELSE v_range && daterange(p.period_from, p.period_to, '[]') END
      AND EXISTS (
          SELECT FROM payroll.members m
          JOIN employment.employments x ON x.id = m.employment_id
          WHERE m.payroll_id = p.id
            AND (m.employment_id = p_employment_id OR (p_employment_id IS NULL AND x.employee_id = p_employee_id))
      );
END;
$$;

--changeset hr:payroll.guard_period_change runOnChange:true splitStatements:false
--comment: Zmiana okresu umowy (004 employment_update, employment_terminate): dni dodane albo odjęte na początku i na
--comment: końcu przechodzą przez payroll.guard — okres zamkniętej listy U0003 payrollClosed (wyjątek korekty jak przy
--comment: innych danych), naliczonej — lista nieaktualna (009 R2, R25). Koniec NULL = bez końca.
CREATE OR REPLACE FUNCTION payroll.guard_period_change(
    p_employee_id uuid,
    p_employment_id uuid,
    p_old_from date,
    p_old_to date,
    p_new_from date,
    p_new_to date
)
    RETURNS void
    LANGUAGE plpgsql
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_old_to date := coalesce(p_old_to, 'infinity');
    v_new_to date := coalesce(p_new_to, 'infinity');
BEGIN
    IF p_old_from <> p_new_from THEN
        PERFORM payroll.guard(p_employee_id, p_employment_id, least(p_old_from, p_new_from),
                              greatest(p_old_from, p_new_from) - 1);
    END IF;
    IF v_old_to <> v_new_to THEN
        PERFORM payroll.guard(p_employee_id, p_employment_id, least(v_old_to, v_new_to) + 1,
                              nullif(greatest(v_old_to, v_new_to), 'infinity'));
    END IF;
END;
$$;

--changeset hr:payroll.guard_used runOnChange:true splitStatements:false
--comment: Blokada usunięcia danych użytych na zamkniętej liście (R25; „od 009” z 004–008): umowa (employment),
--comment: przypisanie składnika (component), zgoda (consent), zajęcie (garnishment), oświadczenie (declaration),
--comment: uczestnictwo PPK (ppk — id kartoteki). U0003 payrollClosed. Umowa na liście nie zamkniętej znika z niej,
--comment: a naliczona lista staje się nieaktualna.
CREATE OR REPLACE FUNCTION payroll.guard_used(p_kind text, p_id uuid)
    RETURNS void
    LANGUAGE plpgsql
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_used boolean;
BEGIN
    SELECT EXISTS (
        SELECT FROM payroll.entries e
        JOIN payroll.payrolls p ON p.id = e.payroll_id AND p.status = 'closed'
        WHERE CASE p_kind
            WHEN 'employment' THEN e.employment_id = p_id
            WHEN 'ppk' THEN e.employee_id = p_id AND EXISTS (
                SELECT FROM payroll.lines l
                WHERE l.entry_id = e.id AND l.code IN ('PPK-P', 'PPK-PD', 'PPK-PR', 'PPK-PRD') AND l.amount <> 0)
            ELSE EXISTS (
                SELECT FROM payroll.lines l
                WHERE l.entry_id = e.id AND CASE p_kind
                    WHEN 'component' THEN l.trace ->> 'employmentComponentId' = p_id::text
                    WHEN 'consent' THEN l.trace ->> 'consentId' = p_id::text
                    WHEN 'garnishment' THEN l.trace ->> 'garnishmentId' = p_id::text
                    WHEN 'declaration' THEN coalesce(l.trace -> 'declarationIds', '[]') ? p_id::text
                    ELSE false
                END)
        END
    ) INTO v_used;
    IF v_used THEN
        RAISE EXCEPTION 'data is used on a closed payroll' USING ERRCODE = 'U0003', HINT = 'payrollClosed';
    END IF;

    IF p_kind = 'employment' THEN
        UPDATE payroll.payrolls p SET is_stale = true
        WHERE p.status = 'calculated'
          AND EXISTS (SELECT FROM payroll.members m WHERE m.payroll_id = p.id AND m.employment_id = p_id);
        DELETE FROM payroll.entries e WHERE e.employment_id = p_id;
        DELETE FROM payroll.members m WHERE m.employment_id = p_id;
    END IF;
END;
$$;
