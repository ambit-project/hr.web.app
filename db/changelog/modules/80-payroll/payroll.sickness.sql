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

--changeset hr:payroll.sickness_base runOnChange:true splitStatements:false
--comment: Podstawa wynagrodzenia chorobowego i zasiłku (R7): średnia z 12 miesięcy kalendarzowych przed miesiącem
--comment: początku ciągu niezdolności z zamkniętych list kartoteki (ten sam rodzaj umowy) — przychód w podstawie
--comment: chorobowego (PODST-CHOR) minus faktycznie potrącone składki emerytalna, rentowa i chorobowa (proporcjonalnie,
--comment: gdy część przychodu ze składkami nie wchodzi do podstawy chorobowego) — z pominięciem miesięcy, w których
--comment: przepracowano mniej niż połowę wymiaru (art. 38 ust. 2) [D: bez uzupełniania]. Bez zamkniętych list —
--comment: wynagrodzenie z warunków i stałe składniki w podstawie chorobowego minus składki pracownika (art. 37) [D].
--comment: Umowa o pracę: nie mniej niż chorobowe.podstawa.minimalna × etat (art. 45). Zlecenie godzinowe bez historii —
--comment: podstawa 0 i ostrzeżenie sicknessBaseMissing.
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
    v_months jsonb;
    v_count integer;
    v_sum numeric;
    v_terms employment.terms%ROWTYPE;
    v_amount numeric;
    v_contributions numeric;
    v_minimum numeric;
BEGIN
    WITH months AS (
        SELECT c.period_from,
               sum(c.amount) FILTER (WHERE c.code = 'PODST-CHOR') AS sickness_income,
               sum(c.amount) FILTER (WHERE c.code = 'PODST-ZUS') AS zus_income,
               sum(c.amount) FILTER (WHERE c.code IN ('ZUS-E-P', 'ZUS-R-P', 'ZUS-C-P')) AS contributions
        FROM payroll.closed_lines(p_ctx.employee_id) c
        WHERE c.contract_kind = p_ctx.contract_kind
          AND c.period_from >= (v_month - interval '12 months')::date AND c.period_from < v_month
        GROUP BY c.period_from
    ), hours AS (
        SELECT p.period_from, sum(e.norm_hours) AS norm_hours, sum(e.worked_hours) AS worked_hours
        FROM payroll.entries e
        JOIN payroll.payrolls p ON p.id = e.payroll_id AND p.status = 'closed' AND p.kind <> 'correction'
        JOIN employment.employments m ON m.id = e.employment_id AND m.kind = p_ctx.contract_kind
        WHERE e.employee_id = p_ctx.employee_id
        GROUP BY p.period_from
    ), valued AS (
        SELECT x.period_from,
               coalesce(x.sickness_income, 0) - coalesce(x.contributions, 0)
                   * CASE WHEN coalesce(x.zus_income, 0) > 0
                          THEN coalesce(x.sickness_income, 0) / x.zus_income ELSE 1 END AS value,
               h.norm_hours, h.worked_hours,
               coalesce(h.worked_hours < h.norm_hours / 2, false) AS skipped
        FROM months x
        LEFT JOIN hours h ON h.period_from = x.period_from
    )
    SELECT jsonb_agg(jsonb_build_object('month', to_char(v.period_from, 'YYYY-MM'), 'value', round(v.value, 2),
                                        'skipped', v.skipped) ORDER BY v.period_from),
           count(*) FILTER (WHERE NOT v.skipped),
           sum(v.value) FILTER (WHERE NOT v.skipped)
    INTO v_months, v_count, v_sum
    FROM valued v;

    IF v_count > 0 THEN
        base := round(v_sum / v_count, 2);
        trace := jsonb_build_object('method', 'average', 'chainStart', p_chain_start, 'months', v_months);
    ELSE
        SELECT * INTO v_terms FROM employment.terms t
        WHERE t.employment_id = p_ctx.employment_id AND daterange(t.valid_from, t.valid_to, '[]') @> p_chain_start;
        IF v_terms.id IS NULL OR v_terms.pay_basis = 'hourly' THEN
            base := 0;
            warning := 'sicknessBaseMissing';
            trace := jsonb_build_object('method', 'missing', 'chainStart', p_chain_start);
            RETURN;
        END IF;
        v_amount := v_terms.base_amount + coalesce((
            SELECT sum(c.value)
            FROM pay_components.employment_components c
            JOIN pay_components.component_types t ON t.id = c.component_type_id
            WHERE c.employment_id = p_ctx.employment_id AND t.kind = 'addition' AND t.calculation = 'fixed'
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
        trace := jsonb_build_object('method', 'terms', 'chainStart', p_chain_start, 'termsId', v_terms.id,
                                    'amount', v_amount, 'contributions', v_contributions,
                                    'months', coalesce(v_months, '[]'));
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
