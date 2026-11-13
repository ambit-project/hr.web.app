--liquibase formatted sql

--changeset hr:payroll.absence_days runOnChange:true splitStatements:false
--comment: Dni nieobecności umowy w okresie z rodzajem wypłaty (007), godzinami kalendarza pełnego etatu i kwotą
--comment: zasadniczego z warunków obowiązujących w dniu (R5, R6, R8).
DROP FUNCTION IF EXISTS payroll.absence_days(uuid, date, date);
CREATE FUNCTION payroll.absence_days(p_employment_id uuid, p_from date, p_to date)
    RETURNS TABLE (
        day date,
        absence_id uuid,
        type_code text,
        payment text,
        sickness boolean,
        hours numeric,
        base_amount numeric
    )
    LANGUAGE sql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT d::date, a.id, a.absence_type_code, ty.payment, ty.sickness, coalesce(c.hours, 0), t.base_amount
    FROM time_absence.absences a
    JOIN employment.employments m ON m.id = a.employment_id
    JOIN time_absence.absence_types ty ON ty.code = a.absence_type_code
    CROSS JOIN LATERAL generate_series(greatest(a.date_from, p_from), least(a.date_to, p_to), interval '1 day') d
    LEFT JOIN time_absence.calendar_days c ON c.company_id = m.company_id AND c.date = d::date AND c.kind = 'workday'
    LEFT JOIN employment.terms t ON t.employment_id = a.employment_id
        AND daterange(t.valid_from, t.valid_to, '[]') @> d::date
    WHERE a.employment_id = p_employment_id AND a.date_from <= p_to AND a.date_to >= p_from;
$$;

--changeset hr:payroll.fixed_due runOnChange:true splitStatements:false
--comment: Kwota stałego składnika miesięcznego umowy o pracę za okres (R10 [D]): pełna, gdy składnik i umowa obejmują
--comment: cały miesiąc, inaczej proporcjonalnie do godzin kalendarza; minus 1/30 za dzień choroby, gdy składnik
--comment: wchodzi do podstawy chorobowego (cały okres w chorobie — 0); minus godziny nieobecności bez wynagrodzenia
--comment: i połowa godzin siły wyższej (jak R8). Nie mniej niż 0.
CREATE OR REPLACE FUNCTION payroll.fixed_due(
    p_ctx payroll.ctx_t,
    p_value numeric,
    p_valid_from date,
    p_valid_to date,
    p_sickness_reduces boolean,
    OUT amount numeric,
    OUT trace jsonb
)
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_from date := greatest(p_ctx.from_date, p_valid_from);
    v_to date := least(p_ctx.to_date, coalesce(p_valid_to, p_ctx.to_date));
    v_month_hours numeric := payroll.calendar_hours(p_ctx.company_id, p_ctx.period_from, p_ctx.period_to);
    v_sick_days integer;
    v_unpaid_hours numeric;
    v_half_hours numeric;
BEGIN
    IF v_from = p_ctx.period_from AND v_to = p_ctx.period_to THEN
        amount := p_value;
    ELSE
        amount := p_value * payroll.calendar_hours(p_ctx.company_id, v_from, v_to) / nullif(v_month_hours, 0);
    END IF;
    SELECT count(*) FILTER (WHERE a.sickness),
           coalesce(sum(a.hours) FILTER (WHERE a.payment = 'unpaid'), 0),
           coalesce(sum(a.hours) FILTER (WHERE a.payment = 'salaryHalf'), 0)
    INTO v_sick_days, v_unpaid_hours, v_half_hours
    FROM payroll.absence_days(p_ctx.employment_id, v_from, v_to) a;
    IF p_sickness_reduces AND v_sick_days > 0 THEN
        IF v_sick_days = v_to - v_from + 1 THEN
            amount := 0;
        ELSE
            amount := amount - p_value * v_sick_days / 30;
        END IF;
    END IF;
    amount := greatest(round(amount - p_value * (v_unpaid_hours + v_half_hours / 2) / nullif(v_month_hours, 0), 2),
                       0);
    trace := jsonb_build_object('from', v_from, 'to', v_to, 'sickDays', v_sick_days, 'unpaidHours', v_unpaid_hours,
                                'forceMajeureHours', v_half_hours, 'monthHours', v_month_hours);
END;
$$;

--changeset hr:payroll.stage_base_pay runOnChange:true splitStatements:false
--comment: Etap 1 — wynagrodzenie zasadnicze umowy o pracę (R5, R6, R8): ZASAD z każdej wersji warunków w okresie
--comment: (cały miesiąc — proporcja dni kalendarzowych [D], część miesiąca — godziny wymiaru, R1996 § 12 ust. 2);
--comment: ZASAD-CH — 1/30 za dzień choroby (R1996 § 11 ust. 1 — tylko przy przepracowanej części miesiąca; cały
--comment: okres w chorobie — całe zasadnicze, bo bez pracy wynagrodzenie nie przysługuje, KP art. 80); ZASAD-NB —
--comment: godziny UB, UOP, NN i SW × wynagrodzenie ÷ wymiar miesiąca (§ 12 ust. 1); SW50 — 50% za godziny siły
--comment: wyższej (KP art. 148¹). Wymiar i godziny — kalendarz spółki pełnego etatu (proporcja ta sama dla etatu).
CREATE OR REPLACE FUNCTION payroll.stage_base_pay(p_ctx payroll.ctx_t, p_lines payroll.line_t[])
    RETURNS payroll.line_t[]
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_lines payroll.line_t[] := p_lines;
    v_month_hours numeric := payroll.calendar_hours(p_ctx.company_id, p_ctx.period_from, p_ctx.period_to);
    v_month_days integer := p_ctx.period_to - p_ctx.period_from + 1;
    v_full boolean := p_ctx.from_date = p_ctx.period_from AND p_ctx.to_date = p_ctx.period_to;
    v_segment record;
    v_hours numeric;
    v_amount numeric;
    v_total numeric := 0;
    v_sick record;
    v_reduction numeric;
    v_unpaid record;
BEGIN
    IF p_ctx.contract_kind <> 'employment' THEN
        RETURN v_lines;
    END IF;
    FOR v_segment IN
        SELECT t.id, t.base_amount, greatest(t.valid_from, p_ctx.from_date) AS seg_from,
               least(coalesce(t.valid_to, p_ctx.to_date), p_ctx.to_date) AS seg_to
        FROM employment.terms t
        WHERE t.employment_id = p_ctx.employment_id
          AND daterange(t.valid_from, t.valid_to, '[]') && daterange(p_ctx.from_date, p_ctx.to_date, '[]')
        ORDER BY t.valid_from
    LOOP
        IF v_full THEN
            v_amount := round(v_segment.base_amount * (v_segment.seg_to - v_segment.seg_from + 1) / v_month_days, 2);
            v_lines := v_lines || payroll.system_income('ZASAD', 'ZASAD', v_segment.base_amount, NULL, v_amount,
                jsonb_build_object('termsId', v_segment.id, 'from', v_segment.seg_from, 'to', v_segment.seg_to,
                                   'method', 'days', 'days', v_segment.seg_to - v_segment.seg_from + 1,
                                   'monthDays', v_month_days));
        ELSE
            v_hours := payroll.calendar_hours(p_ctx.company_id, v_segment.seg_from, v_segment.seg_to);
            v_amount := round(v_segment.base_amount * v_hours / nullif(v_month_hours, 0), 2);
            v_lines := v_lines || payroll.system_income('ZASAD', 'ZASAD', v_segment.base_amount, NULL, v_amount,
                jsonb_build_object('termsId', v_segment.id, 'from', v_segment.seg_from, 'to', v_segment.seg_to,
                                   'method', 'hours', 'hours', v_hours, 'monthHours', v_month_hours));
        END IF;
        v_total := v_total + v_amount;
    END LOOP;

    SELECT count(*) AS days, coalesce(sum(a.base_amount), 0) AS amounts,
           coalesce(jsonb_agg(DISTINCT a.absence_id), '[]') AS ids
    INTO v_sick
    FROM payroll.absence_days(p_ctx.employment_id, p_ctx.from_date, p_ctx.to_date) a
    WHERE a.sickness;
    IF v_sick.days > 0 THEN
        IF v_sick.days = p_ctx.to_date - p_ctx.from_date + 1 THEN
            v_reduction := v_total;
        ELSE
            v_reduction := least(round(v_sick.amounts / 30, 2), v_total);
        END IF;
        v_lines := v_lines || payroll.system_income('ZASAD-CH', 'ZASAD', NULL, NULL, -v_reduction,
            jsonb_build_object('absenceIds', v_sick.ids, 'days', v_sick.days,
                               'rule', CASE WHEN v_reduction = v_total THEN 'wholePeriod' ELSE '1/30' END));
        v_total := v_total - v_reduction;
    END IF;

    SELECT coalesce(sum(a.hours) FILTER (WHERE a.payment IN ('unpaid', 'salaryHalf')), 0) AS hours,
           coalesce(sum(a.base_amount * a.hours) FILTER (WHERE a.payment IN ('unpaid', 'salaryHalf')), 0) AS value,
           coalesce(sum(a.hours) FILTER (WHERE a.payment = 'salaryHalf'), 0) AS half_hours,
           coalesce(sum(a.base_amount * a.hours) FILTER (WHERE a.payment = 'salaryHalf'), 0) AS half_value,
           coalesce(jsonb_agg(DISTINCT a.absence_id) FILTER (WHERE a.payment IN ('unpaid', 'salaryHalf')), '[]') AS ids,
           coalesce(jsonb_agg(DISTINCT a.absence_id) FILTER (WHERE a.payment = 'salaryHalf'), '[]') AS half_ids
    INTO v_unpaid
    FROM payroll.absence_days(p_ctx.employment_id, p_ctx.from_date, p_ctx.to_date) a;
    IF v_unpaid.hours > 0 THEN
        v_reduction := least(round(v_unpaid.value / v_month_hours, 2), v_total);
        v_lines := v_lines || payroll.system_income('ZASAD-NB', 'ZASAD', NULL, NULL, -v_reduction,
            jsonb_build_object('absenceIds', v_unpaid.ids, 'hours', v_unpaid.hours, 'monthHours', v_month_hours));
    END IF;
    IF v_unpaid.half_hours > 0 THEN
        v_lines := v_lines || payroll.system_income('SW50', 'ZASAD', round(v_unpaid.half_value / v_month_hours, 2),
            0.5, round(v_unpaid.half_value / v_month_hours * 0.5, 2),
            jsonb_build_object('absenceIds', v_unpaid.half_ids, 'hours', v_unpaid.half_hours,
                               'monthHours', v_month_hours));
    END IF;
    RETURN v_lines;
END;
$$;

--changeset hr:payroll.stage_absence_pay runOnChange:true splitStatements:false
--comment: Etap 2 — choroba i urlop (R6–R8). Dni choroby kartoteki w roku liczone kolejno: do 33 (14 po roku
--comment: ukończenia 50 lat) — CHOR (wynagrodzenie chorobowe, KP art. 92), dalej zasiłek: ZAS-CH, gdy spółka wypłaca
--comment: zasiłki, inaczej informacyjnie ZAS-ZUS. Zlecenie — od pierwszego dnia zasiłek. Niezdolność w okresie
--comment: wyczekiwania (art. 4: 30 / 90 dni od początku ubezpieczenia u płatnika) albo bez ustalonej podstawy (sickness_base)
--comment: — bez kwot, ostrzeżenie blokujące zamknięcie. Dzienna = podstawa ÷ 30
--comment: (do grosza) × 80% albo 100% (CHW, CHC; do grosza) × dni. URLOP-ZM — średnia godzinowa zmiennych z 3 miesięcy
--comment: × godziny urlopu i płatnych zwolnień (UW, UZ, UO, OD) [D: bez składników procentowych — liczone od
--comment: zasadniczego, które za urlop nie jest pomniejszane].
CREATE OR REPLACE FUNCTION payroll.stage_absence_pay(p_ctx payroll.ctx_t, p_lines payroll.line_t[])
    RETURNS payroll.line_t[]
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_lines payroll.line_t[] := p_lines;
    v_year_start date := date_trunc('year', p_ctx.from_date)::date;
    v_limit integer;
    v_settings record;
    v_absence record;
    v_employer_days integer;
    v_allowance_days integer;
    v_chain date;
    v_base record;
    v_rate numeric;
    v_daily numeric;
    v_daily_rate numeric;
    v_trace jsonb;
    v_leave_hours numeric;
    v_variable record;
BEGIN
    IF p_ctx.contract_kind NOT IN ('employment', 'mandate') THEN
        RETURN v_lines;
    END IF;
    v_limit := CASE
        WHEN extract(year FROM p_ctx.from_date) > extract(year FROM p_ctx.birth_date) + 50
            THEN platform.legal_parameter('chorobowe.pracodawca.dni.50', p_ctx.from_date)
        ELSE platform.legal_parameter('chorobowe.pracodawca.dni', p_ctx.from_date)
    END;

    FOR v_absence IN
        SELECT a.id, a.date_from, a.date_to, a.absence_type_code, ty.payment
        FROM time_absence.absences a
        JOIN time_absence.absence_types ty ON ty.code = a.absence_type_code AND ty.sickness
        WHERE a.employment_id = p_ctx.employment_id AND a.date_from <= p_ctx.to_date AND a.date_to >= p_ctx.from_date
        ORDER BY a.date_from
    LOOP
        IF p_ctx.contract_kind = 'employment' THEN
            SELECT count(*) FILTER (WHERE x.n <= v_limit), count(*) FILTER (WHERE x.n > v_limit)
            INTO v_employer_days, v_allowance_days
            FROM (
                SELECT d::date AS day, a.id, row_number() OVER (ORDER BY d) AS n
                FROM time_absence.absences a
                JOIN time_absence.absence_types ty ON ty.code = a.absence_type_code AND ty.sickness
                JOIN employment.employments m ON m.id = a.employment_id
                CROSS JOIN LATERAL generate_series(greatest(a.date_from, v_year_start), a.date_to, interval '1 day') d
                WHERE m.employee_id = p_ctx.employee_id AND m.kind = 'employment'
                  AND a.date_to >= v_year_start AND a.date_from <= p_ctx.to_date
            ) x
            WHERE x.id = v_absence.id AND x.day BETWEEN p_ctx.from_date AND p_ctx.to_date;
        ELSE
            v_employer_days := 0;
            v_allowance_days := least(v_absence.date_to, p_ctx.to_date) - greatest(v_absence.date_from, p_ctx.from_date)
                + 1;
        END IF;

        -- Okres wyczekiwania (ustawa zasiłkowa art. 4 ust. 1; KP art. 92 § 3): 30 dni ubezpieczenia obowiązkowego,
        -- 90 dobrowolnego. Wcześniejsze ubezpieczenie u innych płatników i wyjątki z art. 4 ust. 2–3 są poza
        -- systemem, więc niezdolność w tym okresie nie jest liczona.
        IF v_absence.date_from < payroll.insured_from(p_ctx.employment_id)
            + (CASE WHEN p_ctx.contract_kind = 'employment' THEN 30 ELSE 90 END) THEN
            v_lines := v_lines || payroll.warning('sicknessWaitingPeriod',
                jsonb_build_object('absenceId', v_absence.id, 'insuredFrom', payroll.insured_from(p_ctx.employment_id)));
            CONTINUE;
        END IF;
        v_chain := payroll.sickness_chain_start(p_ctx.employee_id, p_ctx.contract_kind, v_absence.date_from);
        SELECT * INTO v_base FROM payroll.sickness_base(p_ctx, v_chain);
        IF v_base.warning IS NOT NULL THEN
            v_lines := v_lines || payroll.warning(v_base.warning,
                jsonb_build_object('absenceId', v_absence.id, 'base', v_base.trace));
            CONTINUE;
        END IF;
        v_rate := CASE WHEN v_absence.payment = 'sickness100' THEN 1
                       ELSE platform.legal_parameter('chorobowe.stawka', v_absence.date_from) END;
        v_daily := round(v_base.base / 30, 2);
        v_daily_rate := round(v_daily * v_rate, 2);
        v_trace := jsonb_build_object('absenceId', v_absence.id, 'type', v_absence.absence_type_code,
                                      'dailyBase', v_daily, 'dailyRate', v_daily_rate, 'yearDayLimit', v_limit,
                                      'base', v_base.trace,
                                      'params', payroll.params(ARRAY['chorobowe.stawka', 'chorobowe.pracodawca.dni',
                                          'chorobowe.pracodawca.dni.50', 'chorobowe.podstawa.minimalna'],
                                          v_absence.date_from));
        IF v_employer_days > 0 THEN
            v_lines := v_lines || payroll.system_income('CHOR', 'CHOR', v_base.base, v_rate,
                v_daily_rate * v_employer_days, v_trace || jsonb_build_object('days', v_employer_days));
        END IF;
        IF v_allowance_days > 0 THEN
            SELECT * INTO v_settings FROM payroll.settings_on(p_ctx.company_id, p_ctx.period_from);
            IF v_settings.pays_sickness_allowance THEN
                -- Zasiłek wypłacany przez płatnika: bez składek i zdrowotnej, opodatkowany (art. 12 ust. 1 PIT).
                v_lines := v_lines || payroll.income('ZAS-CH', v_base.base, v_rate, v_daily_rate * v_allowance_days,
                    NULL, false, false, true, false,
                    v_trace || jsonb_build_object('days', v_allowance_days, 'settingsId', v_settings.settings_id));
            ELSE
                v_lines := v_lines || payroll.item('ZAS-ZUS', 'info', v_base.base, v_rate,
                    v_daily_rate * v_allowance_days, v_trace || jsonb_build_object('days', v_allowance_days));
            END IF;
        END IF;
    END LOOP;

    IF p_ctx.contract_kind = 'employment' THEN
        SELECT coalesce(sum(time_absence.working_hours(p_ctx.employment_id, greatest(a.date_from, p_ctx.from_date),
                                                       least(a.date_to, p_ctx.to_date))), 0)
        INTO v_leave_hours
        FROM time_absence.absences a
        JOIN time_absence.absence_types ty ON ty.code = a.absence_type_code AND ty.payment IN ('leavePay', 'salary')
        WHERE a.employment_id = p_ctx.employment_id AND a.date_from <= p_ctx.to_date AND a.date_to >= p_ctx.from_date;
        IF v_leave_hours > 0 THEN
            SELECT * INTO v_variable FROM payroll.variable_pay(p_ctx);
            IF v_variable.hourly > 0 THEN
                -- Wynagrodzenie urlopowe: ZUS, zdrowotna, PIT i podstawa chorobowego jak wynagrodzenie.
                v_lines := v_lines || payroll.income('URLOP-ZM', v_leave_hours, v_variable.hourly,
                    round(v_variable.hourly * v_leave_hours, 2), NULL, true, true, true, true,
                    v_variable.trace || jsonb_build_object('hours', v_leave_hours));
            END IF;
        END IF;
    END IF;
    RETURN v_lines;
END;
$$;

--changeset hr:payroll.stage_overtime runOnChange:true splitStatements:false
--comment: Etap 3 — nadgodziny umowy o pracę (R9): normalne wynagrodzenie i dodatek 50% albo 100% (KP art. 151¹ § 1)
--comment: od wynagrodzenia z osobistego zaszeregowania (§ 3): zasadnicze z dnia ÷ wymiar miesiąca (godziny kalendarza
--comment: × etat; R1996 § 4a) × godziny × 1,5 albo 2,0. Umowa z innymi składnikami dodatkowymi w okresie — nie
--comment: liczone, ostrzeżenie blokujące overtimeNormalPayUnknown.
CREATE OR REPLACE FUNCTION payroll.stage_overtime(p_ctx payroll.ctx_t, p_lines payroll.line_t[])
    RETURNS payroll.line_t[]
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_lines payroll.line_t[] := p_lines;
    v_month_hours numeric := payroll.calendar_hours(p_ctx.company_id, p_ctx.period_from, p_ctx.period_to);
    v_group record;
    v_multiplier numeric;
BEGIN
    IF p_ctx.contract_kind <> 'employment' THEN
        RETURN v_lines;
    END IF;
    IF EXISTS (
        SELECT FROM time_absence.overtime_entries o
        WHERE o.employment_id = p_ctx.employment_id AND o.date BETWEEN p_ctx.from_date AND p_ctx.to_date
    ) AND EXISTS (
        SELECT FROM pay_components.employment_components c
        JOIN pay_components.component_types t ON t.id = c.component_type_id
        WHERE c.employment_id = p_ctx.employment_id AND t.kind = 'addition' AND NOT t.is_system
          AND daterange(c.valid_from, c.valid_to, '[]') && daterange(p_ctx.from_date, p_ctx.to_date, '[]')
    ) THEN
        -- Czy inne składniki wchodzą do „normalnego wynagrodzenia” (KP art. 151¹ § 1) — niesprawdzone.
        RETURN v_lines || payroll.warning('overtimeNormalPayUnknown', '{}');
    END IF;
    FOR v_group IN
        SELECT o.rate, sum(o.hours) AS hours,
               sum(o.hours * t.base_amount / (v_month_hours * coalesce(t.fte_numerator::numeric / t.fte_denominator, 1)))
                   AS value,
               max(t.base_amount / (v_month_hours * coalesce(t.fte_numerator::numeric / t.fte_denominator, 1)))
                   AS hourly,
               jsonb_agg(o.id ORDER BY o.date) AS ids
        FROM time_absence.overtime_entries o
        JOIN employment.terms t ON t.employment_id = o.employment_id
            AND daterange(t.valid_from, t.valid_to, '[]') @> o.date
        WHERE o.employment_id = p_ctx.employment_id AND o.date BETWEEN p_ctx.from_date AND p_ctx.to_date
        GROUP BY o.rate
        ORDER BY o.rate
    LOOP
        v_multiplier := 1 + v_group.rate / 100.0;
        v_lines := v_lines || payroll.system_income('NADG' || v_group.rate, 'NADG' || v_group.rate,
            round(v_group.hourly, 4), v_multiplier, round(v_group.value * v_multiplier, 2),
            jsonb_build_object('overtimeIds', v_group.ids, 'hours', v_group.hours, 'monthHours', v_month_hours));
    END LOOP;
    RETURN v_lines;
END;
$$;

--changeset hr:payroll.stage_components runOnChange:true splitStatements:false
--comment: Etap 4 — wynagrodzenie zlecenia i dzieła, składniki umowy, ekwiwalent (R10–R12). ZLEC: stawka × potwierdzone
--comment: godziny kończące się w okresie (007 R17; niepotwierdzone — ostrzeżenie unconfirmedHours) albo ryczałt
--comment: miesięczny z warunków, w części miesiąca proporcjonalnie do dni [D]. DZIELO: kwota z warunków w miesiącu końca
--comment: umowy. Składniki: kwota stała (umowa o pracę — payroll.fixed_due, inne — pełna), procent zasadniczego (po
--comment: pomniejszeniach; zlecenie — procent ZLEC), stawka × godziny (zlecenie — potwierdzone godziny; umowa o pracę —
--comment: poza zakresem, ostrzeżenie). EKWIW w miesiącu końca umowy o pracę: saldo godzin urlopu × (zasadnicze +
--comment: średnia zmiennych) ÷ (dni pracy roku ÷ 12 × etat) ÷ 8 (rozp. urlopowe § 18–19; bez zaokrąglania kwot
--comment: pośrednich). Umowa ze składnikami dodatkowymi w dniu końca — ekwiwalent nie liczony, ostrzeżenie blokujące
--comment: equivalentComponentsUnknown.
CREATE OR REPLACE FUNCTION payroll.stage_components(p_ctx payroll.ctx_t, p_lines payroll.line_t[])
    RETURNS payroll.line_t[]
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_lines payroll.line_t[] := p_lines;
    v_month_days integer := p_ctx.period_to - p_ctx.period_from + 1;
    v_hours record;
    v_segment record;
    v_component record;
    v_due record;
    v_due_trace jsonb;
    v_base numeric;
    v_amount numeric;
    v_year integer;
    v_balance numeric;
    v_variable record;
    v_coefficient numeric;
    v_daily numeric;
    v_hourly numeric;
BEGIN
    IF p_ctx.contract_kind = 'mandate' THEN
        SELECT coalesce(sum(h.hours) FILTER (WHERE h.confirmed_at IS NOT NULL), 0) AS hours,
               coalesce(sum(h.hours * t.base_amount) FILTER (WHERE h.confirmed_at IS NOT NULL), 0) AS value,
               max(t.base_amount) AS rate,
               count(*) FILTER (WHERE h.confirmed_at IS NULL) AS unconfirmed,
               coalesce(jsonb_agg(h.id) FILTER (WHERE h.confirmed_at IS NOT NULL), '[]') AS ids
        INTO v_hours
        FROM time_absence.mandate_hours h
        JOIN employment.terms t ON t.employment_id = h.employment_id
            AND daterange(t.valid_from, t.valid_to, '[]') @> h.date_to
        WHERE h.employment_id = p_ctx.employment_id AND h.date_to BETWEEN p_ctx.period_from AND p_ctx.period_to;
        IF v_hours.unconfirmed > 0 THEN
            v_lines := v_lines || payroll.warning('unconfirmedHours', jsonb_build_object('count', v_hours.unconfirmed));
        END IF;
        IF p_ctx.pay_basis = 'hourly' THEN
            IF v_hours.hours > 0 THEN
                v_lines := v_lines || payroll.system_income('ZLEC', 'ZLEC', v_hours.hours, v_hours.rate,
                    round(v_hours.value, 2), jsonb_build_object('mandateHoursIds', v_hours.ids, 'termsId', p_ctx.terms_id));
            END IF;
        ELSE
            FOR v_segment IN
                SELECT t.id, t.base_amount, greatest(t.valid_from, p_ctx.from_date) AS seg_from,
                       least(coalesce(t.valid_to, p_ctx.to_date), p_ctx.to_date) AS seg_to
                FROM employment.terms t
                WHERE t.employment_id = p_ctx.employment_id
                  AND daterange(t.valid_from, t.valid_to, '[]') && daterange(p_ctx.from_date, p_ctx.to_date, '[]')
                ORDER BY t.valid_from
            LOOP
                v_lines := v_lines || payroll.system_income('ZLEC', 'ZLEC', v_segment.base_amount, NULL,
                    round(v_segment.base_amount * (v_segment.seg_to - v_segment.seg_from + 1) / v_month_days, 2),
                    jsonb_build_object('termsId', v_segment.id, 'from', v_segment.seg_from, 'to', v_segment.seg_to,
                                       'method', 'days', 'monthDays', v_month_days));
            END LOOP;
        END IF;
    ELSIF p_ctx.contract_kind = 'specific_work' THEN
        IF p_ctx.contract_end BETWEEN p_ctx.period_from AND p_ctx.period_to THEN
            v_lines := v_lines || payroll.system_income('DZIELO', 'DZIELO', p_ctx.base_amount, NULL, p_ctx.base_amount,
                jsonb_build_object('termsId', p_ctx.terms_id, 'contractEnd', p_ctx.contract_end));
        END IF;
    END IF;

    FOR v_component IN
        SELECT c.id, c.value, c.valid_from, c.valid_to, t.id AS type_id, t.code, t.calculation, t.in_zus_base,
               t.in_health_base, t.taxable, t.in_sickness_base
        FROM pay_components.employment_components c
        JOIN pay_components.component_types t ON t.id = c.component_type_id
        WHERE c.employment_id = p_ctx.employment_id AND t.kind = 'addition' AND NOT t.is_system
          AND daterange(c.valid_from, c.valid_to, '[]') && daterange(p_ctx.from_date, p_ctx.to_date, '[]')
        ORDER BY t.code, c.valid_from
    LOOP
        v_amount := NULL;
        v_due_trace := '{}';
        IF v_component.calculation = 'fixed' THEN
            IF p_ctx.contract_kind = 'employment' THEN
                SELECT * INTO v_due FROM payroll.fixed_due(p_ctx, v_component.value, v_component.valid_from,
                    v_component.valid_to, v_component.in_sickness_base);
                v_amount := v_due.amount;
                v_due_trace := v_due.trace;
                v_base := v_component.value;
            ELSE
                v_amount := v_component.value;
                v_base := v_component.value;
            END IF;
        ELSIF v_component.calculation = 'percentOfBase' THEN
            v_base := payroll.sum_lines(v_lines, ARRAY['ZASAD', 'ZASAD-CH', 'ZASAD-NB', 'ZLEC']);
            v_amount := round(v_base * v_component.value / 100, 2);
        ELSIF p_ctx.contract_kind = 'mandate' THEN
            v_base := v_hours.hours;
            v_amount := round(v_hours.hours * v_component.value, 2);
        ELSE
            v_lines := v_lines || payroll.warning('rateTimesHoursNotSupported',
                jsonb_build_object('employmentComponentId', v_component.id, 'code', v_component.code));
        END IF;
        IF v_amount IS NOT NULL AND v_amount <> 0 THEN
            v_lines := v_lines || payroll.income(v_component.code, v_base,
                CASE WHEN v_component.calculation = 'fixed' THEN NULL ELSE v_component.value END, v_amount,
                v_component.type_id, v_component.in_zus_base, v_component.in_health_base, v_component.taxable,
                v_component.in_sickness_base,
                jsonb_build_object('employmentComponentId', v_component.id, 'calculation', v_component.calculation,
                                   'value', v_component.value) || v_due_trace);
        END IF;
    END LOOP;

    IF p_ctx.contract_kind = 'employment' AND p_ctx.contract_end BETWEEN p_ctx.period_from AND p_ctx.period_to THEN
        v_year := extract(year FROM p_ctx.contract_end)::integer;
        IF NOT EXISTS (
            SELECT FROM time_absence.leave_entitlements l
            WHERE l.employment_id = p_ctx.employment_id AND l.year = v_year
        ) THEN
            v_lines := v_lines || payroll.warning('leaveEntitlementMissing', jsonb_build_object('year', v_year));
        ELSE
            v_balance := time_absence.leave_entitled(p_ctx.employment_id, v_year)
                + time_absence.leave_carried(p_ctx.employment_id, v_year)
                - time_absence.leave_used(p_ctx.employment_id, v_year);
            IF v_balance > 0 AND EXISTS (
                SELECT FROM pay_components.employment_components c
                JOIN pay_components.component_types t ON t.id = c.component_type_id
                WHERE c.employment_id = p_ctx.employment_id AND t.kind = 'addition' AND NOT t.is_system
                  AND daterange(c.valid_from, c.valid_to, '[]') @> p_ctx.contract_end
            ) THEN
                -- Składniki umowy w ekwiwalencie (rozp. urlopowe § 15–17) — niesprawdzone, ekwiwalent nie liczony.
                v_lines := v_lines || payroll.warning('equivalentComponentsUnknown', jsonb_build_object('year', v_year));
            ELSIF v_balance > 0 THEN
                SELECT * INTO v_variable FROM payroll.variable_pay(p_ctx);
                -- Rozp. urlopowe § 19 ust. 2: dni pracy roku ÷ 12, bez zaokrąglenia (przepis go nie przewiduje);
                -- ust. 3: przy niepełnym etacie współczynnik proporcjonalnie obniżony. § 18 ust. 2: ekwiwalent za
                -- godzinę = za dzień ÷ dobowa norma (8 h; saldo urlopu w godzinach, KP art. 154²). Kwoty pośrednie
                -- bez zaokrąglania — zaokrąglony jest tylko ekwiwalent.
                v_coefficient := platform.legal_parameter('urlop.ekwiwalent.dni', p_ctx.contract_end) / 12;
                v_daily := (p_ctx.base_amount + v_variable.monthly) / (v_coefficient * p_ctx.fte);
                v_hourly := v_daily / 8;
                -- Ekwiwalent: ZUS, zdrowotna i PIT, bez podstawy chorobowego.
                v_lines := v_lines || payroll.income('EKWIW', v_balance, round(v_hourly, 4),
                    round(v_hourly * v_balance, 2), NULL, true, true, true, false,
                    jsonb_build_object('year', v_year, 'balanceHours', v_balance, 'daily', round(v_daily, 4),
                                       'coefficient', round(v_coefficient, 6),
                                       'variableMonthly', v_variable.monthly, 'variable', v_variable.trace,
                                       'termsId', p_ctx.terms_id,
                                       'params', payroll.params(ARRAY['urlop.ekwiwalent.dni'], p_ctx.contract_end)));
            END IF;
        END IF;
    END IF;
    RETURN v_lines;
END;
$$;
