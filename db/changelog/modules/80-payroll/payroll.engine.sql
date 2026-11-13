--liquibase formatted sql

--changeset hr:payroll.ctx_build runOnChange:true splitStatements:false
--comment: Kontekst naliczenia umowy na liście: okres umowy w okresie listy, ostatnia wersja warunków w okresie,
--comment: dzień stawek PPK i listy pomijane w sumach rocznych (R4: lista korygowana i jej korekty dla ZUS).
CREATE OR REPLACE FUNCTION payroll.ctx_build(p_payroll payroll.payrolls, p_employment_id uuid)
    RETURNS payroll.ctx_t
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_ctx payroll.ctx_t;
    v_employment employment.employments%ROWTYPE;
    v_terms employment.terms%ROWTYPE;
    v_corrected payroll.payrolls%ROWTYPE;
BEGIN
    SELECT * INTO v_employment FROM employment.employments m WHERE m.id = p_employment_id;
    v_ctx.payroll_id := p_payroll.id;
    v_ctx.company_id := p_payroll.company_id;
    v_ctx.mode := CASE WHEN p_payroll.kind = 'correction' THEN 'correction' ELSE 'regular' END;
    v_ctx.employment_id := p_employment_id;
    v_ctx.employee_id := v_employment.employee_id;
    v_ctx.contract_kind := v_employment.kind;
    v_ctx.period_from := p_payroll.period_from;
    v_ctx.period_to := p_payroll.period_to;
    v_ctx.contract_end := coalesce(v_employment.terminated_at, v_employment.end_date);
    v_ctx.from_date := greatest(p_payroll.period_from, v_employment.start_date);
    v_ctx.to_date := least(p_payroll.period_to, coalesce(v_ctx.contract_end, p_payroll.period_to));
    IF v_ctx.from_date > v_ctx.to_date THEN
        -- Zlecenie z godzinami po końcu umowy w okresie albo dzieło — dzień końca umowy.
        v_ctx.from_date := least(v_ctx.to_date, v_ctx.from_date);
    END IF;
    v_ctx.payment_date := p_payroll.payment_date;
    v_ctx.ppk_date := p_payroll.payment_date;
    v_ctx.zus_exclude := ARRAY[p_payroll.id];
    v_ctx.pit_exclude := ARRAY[p_payroll.id];
    v_ctx.costs_override := 0;
    IF p_payroll.kind = 'correction' THEN
        SELECT * INTO v_corrected FROM payroll.payrolls p WHERE p.id = p_payroll.corrects_payroll_id;
        v_ctx.ppk_date := v_corrected.payment_date;
        v_ctx.zus_exclude := v_ctx.zus_exclude || v_corrected.id || coalesce((
            SELECT array_agg(c.id) FROM payroll.payrolls c WHERE c.corrects_payroll_id = v_corrected.id
        ), '{}');
    END IF;
    SELECT e.birth_date INTO v_ctx.birth_date FROM employees.employees e WHERE e.id = v_employment.employee_id;

    SELECT * INTO v_terms FROM employment.terms t
    WHERE t.employment_id = p_employment_id AND t.valid_from <= v_ctx.to_date
    ORDER BY t.valid_from DESC
    LIMIT 1;
    v_ctx.terms_id := v_terms.id;
    v_ctx.pay_basis := v_terms.pay_basis;
    v_ctx.base_amount := v_terms.base_amount;
    v_ctx.fte := coalesce(v_terms.fte_numerator::numeric / v_terms.fte_denominator, 1);
    v_ctx.insurances := coalesce(v_terms.insurances, '{}');
    v_ctx.cost_kind := v_terms.cost_kind;
    RETURN v_ctx;
END;
$$;

--changeset hr:payroll.compute runOnChange:true splitStatements:false
--comment: Etapy 1–7 naliczenia (przychody, składki, PPK, zdrowotna) — wspólne dla listy zwykłej i korekty.
CREATE OR REPLACE FUNCTION payroll.compute(p_ctx payroll.ctx_t)
    RETURNS payroll.line_t[]
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_lines payroll.line_t[] := '{}';
BEGIN
    v_lines := payroll.stage_base_pay(p_ctx, v_lines);
    v_lines := payroll.stage_absence_pay(p_ctx, v_lines);
    v_lines := payroll.stage_overtime(p_ctx, v_lines);
    v_lines := payroll.stage_components(p_ctx, v_lines);
    v_lines := payroll.stage_social(p_ctx, v_lines);
    v_lines := payroll.stage_ppk(p_ctx, v_lines);
    RETURN payroll.stage_health(p_ctx, v_lines);
END;
$$;

--changeset hr:payroll.prior_lines runOnChange:true splitStatements:false
--comment: Pozycje umowy już ujęte za okres korekty (R4): lista korygowana i jej zamknięte korekty (bez naliczanej),
--comment: zsumowane po kodzie i stronie.
CREATE OR REPLACE FUNCTION payroll.prior_lines(p_corrected_id uuid, p_employment_id uuid, p_current_id uuid)
    RETURNS payroll.line_t[]
    LANGUAGE sql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT coalesce(array_agg(ROW(x.code, x.side, x.base, x.rate, x.amount, x.type_id, x.zus, x.health, x.taxable,
                                  x.sickness, '{}'::jsonb)::payroll.line_t ORDER BY x.seq), '{}')
    FROM (
        SELECT l.code, l.side, sum(l.base) AS base, max(l.rate) AS rate, sum(l.amount) AS amount,
               (array_agg(l.component_type_id))[1] AS type_id, bool_or(l.in_zus_base) AS zus,
               bool_or(l.in_health_base) AS health, bool_or(l.taxable) AS taxable,
               bool_or(l.in_sickness_base) AS sickness, min(l.seq) AS seq
        FROM payroll.payrolls p
        JOIN payroll.entries e ON e.payroll_id = p.id AND e.employment_id = p_employment_id
        JOIN payroll.lines l ON l.entry_id = e.id
        WHERE (p.id = p_corrected_id OR (p.corrects_payroll_id = p_corrected_id AND p.status = 'closed'))
          AND p.id <> p_current_id
        GROUP BY l.code, l.side
    ) x;
$$;

--changeset hr:payroll.tax_codes runOnChange:true splitStatements:false
--comment: Kody pozycji etapu podatku (liczone przy korekcie od różnic, nie różnicowane).
CREATE OR REPLACE FUNCTION payroll.tax_codes()
    RETURNS text[]
    LANGUAGE sql
    IMMUTABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT ARRAY['KUP', 'KUP50', 'PODST-PIT', 'ZWOL-MLODZI', 'PIT-ZMN', 'PIT', 'PIT-RYCZ'];
$$;

--changeset hr:payroll.diff_lines runOnChange:true splitStatements:false
--comment: Różnice pozycji korekty (R4): nowe naliczenie okresu minus pozycje już ujęte, po kodzie i stronie, bez pozycji
--comment: podatku (liczone od różnic w miesiącu wypłaty korekty) i potrąceń; podstawa różnicowana tylko dla składek.
--comment: Ostrzeżenia nowego naliczenia zostają.
CREATE OR REPLACE FUNCTION payroll.diff_lines(p_new payroll.line_t[], p_prior payroll.line_t[], p_corrected_id uuid)
    RETURNS payroll.line_t[]
    LANGUAGE sql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    WITH fresh AS (
        SELECT l.code, l.side, sum(l.base) AS base, max(l.rate) AS rate, sum(l.amount) AS amount,
               (array_agg(l.component_type_id))[1] AS type_id, bool_or(l.in_zus_base) AS zus,
               bool_or(l.in_health_base) AS health, bool_or(l.taxable) AS taxable,
               bool_or(l.in_sickness_base) AS sickness, min(n) AS ord
        FROM unnest(p_new) WITH ORDINALITY AS l(code, side, base, rate, amount, component_type_id, in_zus_base,
                                                 in_health_base, taxable, in_sickness_base, trace, n)
        WHERE l.side NOT IN ('warning', 'deduction') AND NOT l.code = ANY (payroll.tax_codes())
        GROUP BY l.code, l.side
    ), prior AS (
        SELECT l.code, l.side, l.base, l.rate, l.amount, l.component_type_id AS type_id, l.in_zus_base AS zus,
               l.in_health_base AS health, l.taxable, l.in_sickness_base AS sickness, n AS ord
        FROM unnest(p_prior) WITH ORDINALITY AS l(code, side, base, rate, amount, component_type_id, in_zus_base,
                                                   in_health_base, taxable, in_sickness_base, trace, n)
        WHERE l.side <> 'deduction' AND NOT l.code = ANY (payroll.tax_codes())
    ), diff AS (
        SELECT coalesce(f.code, p.code) AS code, coalesce(f.side, p.side) AS side,
               CASE WHEN coalesce(f.side, p.side) IN ('employee', 'employer')
                    THEN coalesce(f.base, 0) - coalesce(p.base, 0) END AS base,
               coalesce(f.rate, p.rate) AS rate,
               coalesce(f.amount, 0) - coalesce(p.amount, 0) AS amount,
               coalesce(f.type_id, p.type_id) AS type_id,
               CASE WHEN coalesce(f.side, p.side) = 'income' THEN coalesce(f.zus, p.zus) END AS zus,
               CASE WHEN coalesce(f.side, p.side) = 'income' THEN coalesce(f.health, p.health) END AS health,
               CASE WHEN coalesce(f.side, p.side) = 'income' THEN coalesce(f.taxable, p.taxable) END AS taxable,
               CASE WHEN coalesce(f.side, p.side) = 'income' THEN coalesce(f.sickness, p.sickness) END AS sickness,
               jsonb_build_object('correctionOf', p_corrected_id, 'recalculated', coalesce(f.amount, 0),
                                  'previous', coalesce(p.amount, 0)) AS trace,
               coalesce(f.ord, 1000 + p.ord) AS ord
        FROM fresh f
        FULL JOIN prior p ON p.code = f.code AND p.side = f.side
    )
    SELECT coalesce(array_agg(x.line ORDER BY x.ord), '{}')
    FROM (
        SELECT ROW(d.code, d.side, d.base, d.rate, d.amount, d.type_id, d.zus, d.health, d.taxable, d.sickness,
                   d.trace)::payroll.line_t AS line, d.ord
        FROM diff d
        WHERE d.amount <> 0 OR coalesce(d.base, 0) <> 0
        UNION ALL
        SELECT ROW(l.code, l.side, l.base, l.rate, l.amount, l.component_type_id, l.in_zus_base, l.in_health_base,
                   l.taxable, l.in_sickness_base, l.trace)::payroll.line_t, 10000 + n
        FROM unnest(p_new) WITH ORDINALITY AS l(code, side, base, rate, amount, component_type_id, in_zus_base,
                                                 in_health_base, taxable, in_sickness_base, trace, n)
        WHERE l.side = 'warning'
    ) x;
$$;

--changeset hr:payroll.calc_entry runOnChange:true splitStatements:false
--comment: Wypłata umowy na liście (R5–R24): etapy 1–7, podatek, potrącenia; korekta — różnice z pozycjami już ujętymi
--comment: i podatek od różnic, bez potrąceń [D]. Zapisuje wypłatę z kwotami (brutto = przychody, netto = brutto −
--comment: pozycje pracownika, do wypłaty = netto − potrącenia), godzinami i ostrzeżeniami. Korekta bez różnic — bez
--comment: wypłaty (NULL).
CREATE OR REPLACE FUNCTION payroll.calc_entry(p_payroll payroll.payrolls, p_employment_id uuid)
    RETURNS uuid
    LANGUAGE plpgsql
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_ctx payroll.ctx_t := payroll.ctx_build(p_payroll, p_employment_id);
    v_lines payroll.line_t[];
    v_prior payroll.line_t[];
    v_full_costs numeric;
    v_kinds text[];
    v_entry_id uuid;
    v_gross numeric;
    v_net numeric;
    v_norm numeric;
    v_worked numeric;
    v_overtime numeric;
BEGIN
    v_lines := payroll.compute(v_ctx);
    IF v_ctx.mode = 'regular' THEN
        v_lines := payroll.stage_deductions(v_ctx, payroll.stage_tax(v_ctx, v_lines));
    ELSE
        v_prior := payroll.prior_lines(p_payroll.corrects_payroll_id, p_employment_id, p_payroll.id);
        SELECT coalesce(array_agg(d.kind), '{}') INTO v_kinds
        FROM tax_declarations.in_force(v_ctx.employee_id, v_ctx.payment_date) d;
        SELECT c.costs INTO v_full_costs
        FROM payroll.employee_costs(v_ctx,
            payroll.sum_lines(ARRAY(SELECT l FROM unnest(v_lines) l WHERE l.side = 'income' AND l.taxable), '{}')
                + payroll.sum_lines(v_lines, ARRAY['PPK-PR', 'PPK-PRD']),
            payroll.sum_lines(v_lines, ARRAY['ZUS-E-P', 'ZUS-R-P', 'ZUS-C-P']), v_kinds, 0) c;
        v_ctx.costs_override := v_full_costs - payroll.sum_lines(v_prior, ARRAY['KUP', 'KUP50']);
        v_lines := payroll.diff_lines(v_lines, v_prior, p_payroll.corrects_payroll_id);
        IF NOT EXISTS (SELECT FROM unnest(v_lines) l WHERE l.side <> 'warning') AND v_ctx.costs_override = 0 THEN
            RETURN NULL;
        END IF;
        v_lines := payroll.stage_tax(v_ctx, v_lines);
        IF payroll.sum_lines(v_lines, '{}', 'income') - payroll.sum_lines(v_lines, '{}', 'employee') < 0 THEN
            v_lines := v_lines || payroll.warning('negativeCorrection', '{}');
        END IF;
        IF EXISTS (
            SELECT FROM pay_components.garnishments g
            WHERE g.employee_id = v_ctx.employee_id AND g.status = 'active' AND g.received_at <= v_ctx.payment_date
        ) THEN
            v_lines := v_lines || payroll.warning('deductionsNotRecalculated', '{}');
        END IF;
    END IF;

    v_gross := payroll.sum_lines(v_lines, '{}', 'income');
    v_net := v_gross - payroll.sum_lines(v_lines, '{}', 'employee');
    IF v_ctx.mode = 'regular' AND v_ctx.contract_kind = 'employment' THEN
        v_norm := round(payroll.calendar_hours(v_ctx.company_id, v_ctx.period_from, v_ctx.period_to) * v_ctx.fte, 2);
        v_worked := time_absence.working_hours(p_employment_id, v_ctx.from_date, v_ctx.to_date)
            - coalesce((
                SELECT sum(time_absence.working_hours(p_employment_id, greatest(a.date_from, v_ctx.from_date),
                                                      least(a.date_to, v_ctx.to_date)))
                FROM time_absence.absences a
                WHERE a.employment_id = p_employment_id AND a.date_from <= v_ctx.to_date
                  AND a.date_to >= v_ctx.from_date
            ), 0);
        v_overtime := coalesce((
            SELECT sum(o.hours) FROM time_absence.overtime_entries o
            WHERE o.employment_id = p_employment_id AND o.date BETWEEN v_ctx.from_date AND v_ctx.to_date
        ), 0);
    END IF;

    INSERT INTO payroll.entries (payroll_id, employment_id, employee_id, terms_id, gross, net, payout, norm_hours,
                                 worked_hours, overtime_hours, warnings)
    VALUES (p_payroll.id, p_employment_id, v_ctx.employee_id, v_ctx.terms_id, v_gross, v_net,
            v_net - payroll.sum_lines(v_lines, '{}', 'deduction'), v_norm, v_worked, v_overtime,
            coalesce((SELECT jsonb_agg(jsonb_build_object('code', l.code) || l.trace)
                      FROM unnest(v_lines) l WHERE l.side = 'warning'), '[]'))
    RETURNING id INTO v_entry_id;

    INSERT INTO payroll.lines (entry_id, seq, code, side, base, rate, amount, component_type_id, in_zus_base,
                               in_health_base, taxable, in_sickness_base, trace)
    SELECT v_entry_id, row_number() OVER (ORDER BY l.n), l.code, l.side, l.base, l.rate, round(l.amount, 2),
           l.component_type_id, l.in_zus_base, l.in_health_base, l.taxable, l.in_sickness_base, l.trace
    FROM unnest(v_lines) WITH ORDINALITY AS l(code, side, base, rate, amount, component_type_id, in_zus_base,
                                              in_health_base, taxable, in_sickness_base, trace, n)
    WHERE l.side <> 'warning';
    RETURN v_entry_id;
END;
$$;

--changeset hr:payroll.members_sync runOnChange:true splitStatements:false
--comment: Skład zwykłej listy (R3): dopisuje umowy spełniające warunek, usuwa te, które go już nie spełniają;
--comment: wyłączenia Kadr zostają.
CREATE OR REPLACE FUNCTION payroll.members_sync(p_payroll payroll.payrolls)
    RETURNS void
    LANGUAGE plpgsql
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    DELETE FROM payroll.members m
    WHERE m.payroll_id = p_payroll.id
      AND NOT m.employment_id IN (SELECT payroll.eligible_employments(p_payroll.company_id, p_payroll.kind,
                                                                      p_payroll.period_from, p_payroll.period_to));
    INSERT INTO payroll.members (payroll_id, employment_id)
    SELECT p_payroll.id, x.id
    FROM payroll.eligible_employments(p_payroll.company_id, p_payroll.kind, p_payroll.period_from,
                                      p_payroll.period_to) AS x(id)
    ON CONFLICT DO NOTHING;
END;
$$;

--changeset hr:payroll.calculate runOnChange:true splitStatements:false
--comment: Naliczenie listy (R2): odświeża skład zwykłej listy, usuwa poprzednie wypłaty i liczy każdą nie wyłączoną
--comment: umowę; lista naliczona i aktualna. Umowy o pracę wymagają kalendarza spółki (U0003 calendarMissing).
CREATE OR REPLACE FUNCTION payroll.calculate(p_payroll_id uuid)
    RETURNS void
    LANGUAGE plpgsql
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_payroll payroll.payrolls%ROWTYPE;
    v_kind text;
    v_member record;
BEGIN
    SELECT * INTO v_payroll FROM payroll.payrolls p WHERE p.id = p_payroll_id;
    v_kind := CASE WHEN v_payroll.kind = 'correction'
                   THEN (SELECT c.kind FROM payroll.payrolls c WHERE c.id = v_payroll.corrects_payroll_id)
                   ELSE v_payroll.kind END;
    IF v_kind = 'employment' AND time_absence.calendar_missing(v_payroll.company_id, v_payroll.period_from,
                                                               v_payroll.period_to) THEN
        RAISE EXCEPTION 'company has no calendar for the period' USING ERRCODE = 'U0003', HINT = 'calendarMissing';
    END IF;
    IF v_payroll.kind <> 'correction' THEN
        PERFORM payroll.members_sync(v_payroll);
    END IF;
    DELETE FROM payroll.entries e WHERE e.payroll_id = p_payroll_id;
    FOR v_member IN
        SELECT m.employment_id
        FROM payroll.members m
        JOIN employment.employments x ON x.id = m.employment_id
        JOIN employees.employees e ON e.id = x.employee_id
        WHERE m.payroll_id = p_payroll_id AND NOT m.excluded
        ORDER BY e.last_name, e.first_name, x.number
    LOOP
        PERFORM payroll.calc_entry(v_payroll, v_member.employment_id);
    END LOOP;
    UPDATE payroll.payrolls p
    SET status = 'calculated', is_stale = false, calculated_at = now()
    WHERE p.id = p_payroll_id;
END;
$$;
