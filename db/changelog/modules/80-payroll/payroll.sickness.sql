--liquibase formatted sql

--changeset hr:payroll.sickness_chain_start runOnChange:true splitStatements:false
--comment: Początek ciągu niezdolności (R7; ustawa zasiłkowa art. 43): poprzednia choroba kartoteki tego samego rodzaju
--comment: umowy, po której przerwa była krótsza niż miesiąc kalendarzowy, przedłuża ciąg — podstawa ta sama.
CREATE OR REPLACE FUNCTION payroll.sickness_chain_start(p_employee_id uuid, p_contract_kind text, p_from date)
    RETURNS date
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_start date := p_from;
    v_previous date;
BEGIN
    LOOP
        SELECT a.date_from INTO v_previous
        FROM time_absence.absences a
        JOIN time_absence.absence_types t ON t.code = a.absence_type_code AND t.sickness
        JOIN employment.employments m ON m.id = a.employment_id
        WHERE m.employee_id = p_employee_id AND m.kind = p_contract_kind
          AND a.date_from < v_start
          AND v_start < ((a.date_to + 1) + interval '1 month')::date
        ORDER BY a.date_from
        LIMIT 1;
        EXIT WHEN v_previous IS NULL;
        v_start := v_previous;
    END LOOP;
    RETURN v_start;
END;
$$;

--changeset hr:payroll.insured_from runOnChange:true splitStatements:false
--comment: Początek nieprzerwanego ubezpieczenia u płatnika (ustawa zasiłkowa art. 36 ust. 4) — początek umowy, cofnięty
--comment: do początku wcześniejszej umowy tego samego rodzaju kartoteki, która trwała do dnia przed nią.
CREATE OR REPLACE FUNCTION payroll.insured_from(p_employment_id uuid)
    RETURNS date
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_employment employment.employments%ROWTYPE;
    v_start date;
    v_previous date;
BEGIN
    SELECT * INTO v_employment FROM employment.employments m WHERE m.id = p_employment_id;
    v_start := v_employment.start_date;
    LOOP
        SELECT m.start_date INTO v_previous
        FROM employment.employments m
        WHERE m.employee_id = v_employment.employee_id AND m.kind = v_employment.kind AND m.start_date < v_start
          AND coalesce(m.terminated_at, m.end_date, 'infinity') >= v_start - 1
        ORDER BY m.start_date
        LIMIT 1;
        EXIT WHEN v_previous IS NULL;
        v_start := v_previous;
    END LOOP;
    RETURN v_start;
END;
$$;

--changeset hr:payroll.sickness_base runOnChange:true splitStatements:false
--comment: Podstawa wynagrodzenia chorobowego i zasiłku (R7; ustawa zasiłkowa art. 36–38, 40, 45, 48–49). Okres: 12
--comment: miesięcy kalendarzowych przed miesiącem początku ciągu niezdolności, a przy krótszym ubezpieczeniu — pełne
--comment: miesiące ubezpieczenia u płatnika (art. 36 ust. 2 i 4). Wartość miesiąca z zamkniętych list: przychód
--comment: w podstawie chorobowego (PODST-CHOR) minus potrącone składki emerytalna, rentowa i chorobowa (art. 3 pkt 3).
--comment: Umowa o pracę: miesiące z przepracowaną mniej niż połową wymiaru pomijane (art. 38 ust. 2 pkt 1). Zlecenie:
--comment: miesiąc bez wypłaty na zamkniętej liście spółki — bez przychodu, pomijany (art. 38 ust. 1 przez art. 48).
--comment: Przed pełnym miesiącem ubezpieczenia (art. 37 ust. 2 pkt 1, art. 49 ust. 1 pkt 2): wynagrodzenie stałe
--comment: z warunków i stałe składniki minus składki pracownika. System nie liczy (podstawa 0, ostrzeżenie blokujące
--comment: zamknięcie): brak zamkniętej listy za miesiąc okresu (historia poza systemem) albo stawka godzinowa i składniki
--comment: stawka × godziny przed pełnym miesiącem — sicknessBaseUnknown; miesiąc z co najmniej połową czasu pracy
--comment: i wynagrodzeniem obniżonym nieobecnością albo wszystkie miesiące poniżej połowy — wymagane uzupełnienie (art. 38
--comment: ust. 2 pkt 2, ust. 3) — sicknessBaseSupplement; zmiana etatu w okresie (art. 40) — sicknessBaseFteChange.
--comment: Umowa o pracę: nie mniej niż chorobowe.podstawa.minimalna × etat (art. 45).
CREATE OR REPLACE FUNCTION payroll.sickness_base(
    p_ctx payroll.ctx_t,
    p_chain_start date,
    OUT base numeric,
    OUT trace jsonb,
    OUT warning text
)
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_month date := date_trunc('month', p_chain_start)::date;
    v_insured_from date := payroll.insured_from(p_ctx.employment_id);
    v_first_full date;
    v_window_from date;
    v_months jsonb;
    v_count integer;
    v_sum numeric;
    v_missing integer;
    v_supplement boolean;
    v_terms employment.terms%ROWTYPE;
    v_amount numeric;
    v_contributions numeric;
    v_minimum numeric;
BEGIN
    base := 0;
    v_first_full := CASE WHEN v_insured_from = date_trunc('month', v_insured_from)::date THEN v_insured_from
                         ELSE (date_trunc('month', v_insured_from) + interval '1 month')::date END;
    v_window_from := greatest((v_month - interval '12 months')::date, v_first_full);
    trace := jsonb_build_object('chainStart', p_chain_start, 'insuredFrom', v_insured_from);

    IF v_window_from < v_month THEN
        WITH window_months AS (
            SELECT g::date AS period_from
            FROM generate_series(v_window_from, (v_month - interval '1 month')::date, interval '1 month') g
        ), lists AS (
            SELECT p.period_from
            FROM payroll.payrolls p
            WHERE p.company_id = p_ctx.company_id AND p.status = 'closed'
              AND p.kind = CASE p_ctx.contract_kind WHEN 'employment' THEN 'employment' ELSE 'mandate' END
            GROUP BY p.period_from
        ), sums AS (
            SELECT c.period_from,
                   sum(c.amount) FILTER (WHERE c.code = 'PODST-CHOR') AS sickness_income,
                   sum(c.amount) FILTER (WHERE c.code = 'PODST-ZUS') AS zus_income,
                   sum(c.amount) FILTER (WHERE c.code IN ('ZUS-E-P', 'ZUS-R-P', 'ZUS-C-P')) AS contributions,
                   bool_or(c.code IN ('ZASAD-CH', 'ZASAD-NB', 'SW50') AND c.amount <> 0) AS reduced
            FROM payroll.closed_lines(p_ctx.employee_id) c
            WHERE c.contract_kind = p_ctx.contract_kind
              AND c.period_from >= v_window_from AND c.period_from < v_month
            GROUP BY c.period_from
        ), hours AS (
            SELECT p.period_from, sum(e.norm_hours) AS norm_hours, sum(e.worked_hours) AS worked_hours
            FROM payroll.entries e
            JOIN payroll.payrolls p ON p.id = e.payroll_id AND p.status = 'closed' AND p.kind <> 'correction'
            JOIN employment.employments m ON m.id = e.employment_id AND m.kind = p_ctx.contract_kind
            WHERE e.employee_id = p_ctx.employee_id
            GROUP BY p.period_from
        ), valued AS (
            SELECT w.period_from,
                   l.period_from IS NOT NULL AS has_list,
                   s.period_from IS NOT NULL AS has_entry,
                   coalesce(s.sickness_income, 0) - coalesce(s.contributions, 0)
                       * CASE WHEN coalesce(s.zus_income, 0) > 0
                              THEN coalesce(s.sickness_income, 0) / s.zus_income ELSE 1 END AS value,
                   coalesce(s.reduced, false) AS reduced,
                   CASE WHEN p_ctx.contract_kind = 'employment'
                        THEN coalesce(h.worked_hours < h.norm_hours / 2, false)
                        ELSE s.period_from IS NULL END AS skipped
            FROM window_months w
            LEFT JOIN lists l ON l.period_from = w.period_from
            LEFT JOIN sums s ON s.period_from = w.period_from
            LEFT JOIN hours h ON h.period_from = w.period_from
        )
        SELECT jsonb_agg(jsonb_build_object('month', to_char(v.period_from, 'YYYY-MM'), 'value', round(v.value, 2),
                                            'skipped', v.skipped, 'reduced', v.reduced,
                                            'known', v.has_list AND (v.has_entry OR p_ctx.contract_kind <> 'employment'))
                         ORDER BY v.period_from),
               count(*) FILTER (WHERE NOT v.skipped),
               sum(v.value) FILTER (WHERE NOT v.skipped),
               count(*) FILTER (WHERE NOT v.has_list OR (p_ctx.contract_kind = 'employment' AND NOT v.has_entry)),
               p_ctx.contract_kind = 'employment'
                   AND (bool_and(v.skipped) OR bool_or(v.reduced AND NOT v.skipped))
        INTO v_months, v_count, v_sum, v_missing, v_supplement
        FROM valued v;
        trace := trace || jsonb_build_object('method', 'average', 'months', v_months);

        IF v_missing > 0 THEN
            warning := 'sicknessBaseUnknown';
            RETURN;
        END IF;
        IF v_supplement THEN
            warning := 'sicknessBaseSupplement';
            RETURN;
        END IF;
        IF p_ctx.contract_kind = 'employment' AND EXISTS (
            SELECT FROM employment.terms t
            WHERE t.employment_id = p_ctx.employment_id
              AND daterange(t.valid_from, t.valid_to, '[]') && daterange(v_window_from, p_chain_start, '[]')
              AND coalesce(t.fte_numerator::numeric / t.fte_denominator, 1) <> p_ctx.fte
        ) THEN
            warning := 'sicknessBaseFteChange';
            RETURN;
        END IF;
        IF v_count = 0 THEN
            -- Zlecenie bez przychodu w całym okresie — przepisy o tym przypadku nie zostały sprawdzone.
            warning := 'sicknessBaseUnknown';
            RETURN;
        END IF;
        base := round(v_sum / v_count, 2);
    ELSE
        SELECT * INTO v_terms FROM employment.terms t
        WHERE t.employment_id = p_ctx.employment_id AND daterange(t.valid_from, t.valid_to, '[]') @> p_chain_start;
        IF v_terms.id IS NULL OR v_terms.pay_basis = 'hourly' OR EXISTS (
            SELECT FROM pay_components.employment_components c
            JOIN pay_components.component_types t ON t.id = c.component_type_id
            WHERE c.employment_id = p_ctx.employment_id AND t.kind = 'addition' AND t.calculation = 'rateTimesHours'
              AND t.in_sickness_base AND daterange(c.valid_from, c.valid_to, '[]') @> p_chain_start
        ) THEN
            warning := CASE WHEN p_ctx.contract_kind = 'employment' THEN 'sicknessBaseUnknown'
                            ELSE 'sicknessBaseMissing' END;
            trace := trace || jsonb_build_object('method', 'missing');
            RETURN;
        END IF;
        v_amount := v_terms.base_amount + coalesce((
            SELECT sum(CASE WHEN t.calculation = 'fixed' THEN c.value ELSE round(v_terms.base_amount * c.value / 100, 2)
                       END)
            FROM pay_components.employment_components c
            JOIN pay_components.component_types t ON t.id = c.component_type_id
            WHERE c.employment_id = p_ctx.employment_id AND t.kind = 'addition'
              AND t.calculation IN ('fixed', 'percentOfBase')
              AND t.in_sickness_base AND daterange(c.valid_from, c.valid_to, '[]') @> p_chain_start
        ), 0);
        v_contributions :=
            CASE WHEN 'pension' = ANY (v_terms.insurances)
                 THEN round(v_amount * platform.legal_parameter('zus.emerytalna.pracownik', p_chain_start), 2)
                 ELSE 0 END
            + CASE WHEN 'disability' = ANY (v_terms.insurances)
                   THEN round(v_amount * platform.legal_parameter('zus.rentowa.pracownik', p_chain_start), 2)
                   ELSE 0 END
            + CASE WHEN 'sickness' = ANY (v_terms.insurances)
                   THEN round(v_amount * platform.legal_parameter('zus.chorobowa.pracownik', p_chain_start), 2)
                   ELSE 0 END;
        base := v_amount - v_contributions;
        trace := trace || jsonb_build_object('method', 'terms', 'termsId', v_terms.id, 'amount', v_amount,
                                             'contributions', v_contributions);
    END IF;

    IF p_ctx.contract_kind = 'employment' THEN
        v_minimum := round(platform.legal_parameter('chorobowe.podstawa.minimalna', p_chain_start) * p_ctx.fte, 2);
        IF base < v_minimum THEN
            trace := trace || jsonb_build_object('minimum', v_minimum, 'computed', base);
            base := v_minimum;
        END IF;
    END IF;
END;
$$;

--changeset hr:payroll.variable_pay runOnChange:true splitStatements:false
--comment: Składniki zmienne z 3 zamkniętych miesięcy przed okresem (R8, R11; rozp. urlopowe § 8–9, § 18–19)
--comment: [D: bez wydłużania do 12 miesięcy]: nadgodziny i składniki stawka × godziny tej umowy. Średnia godzinowa =
--comment: suma ÷ godziny przepracowane (z nadgodzinami); miesięczna = suma ÷ liczba miesięcy z listą.
CREATE OR REPLACE FUNCTION payroll.variable_pay(
    p_ctx payroll.ctx_t,
    OUT hourly numeric,
    OUT monthly numeric,
    OUT trace jsonb
)
    LANGUAGE sql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    WITH months AS (
        SELECT p.period_from,
               coalesce(sum(l.amount) FILTER (WHERE l.code IN ('NADG50', 'NADG100')
                                              OR l.trace ->> 'calculation' = 'rateTimesHours'), 0) AS variable,
               max(coalesce(e.worked_hours, 0) + coalesce(e.overtime_hours, 0))
                   FILTER (WHERE p.kind <> 'correction') AS hours
        FROM payroll.entries e
        JOIN payroll.payrolls p ON p.id = e.payroll_id AND p.status = 'closed'
        LEFT JOIN payroll.lines l ON l.entry_id = e.id
        WHERE e.employment_id = p_ctx.employment_id
          AND p.period_from >= (p_ctx.period_from - interval '3 months')::date AND p.period_from < p_ctx.period_from
        GROUP BY p.period_from
    )
    SELECT CASE WHEN sum(x.hours) > 0 THEN round(sum(x.variable) / sum(x.hours), 4) ELSE 0 END,
           CASE WHEN count(*) > 0 THEN round(sum(x.variable) / count(*), 2) ELSE 0 END,
           jsonb_build_object('months', coalesce(jsonb_agg(jsonb_build_object(
               'month', to_char(x.period_from, 'YYYY-MM'), 'variable', x.variable, 'hours', x.hours)
               ORDER BY x.period_from), '[]'))
    FROM months x;
$$;
