--liquibase formatted sql

--changeset hr:payroll.employee_costs runOnChange:true splitStatements:false
--comment: Koszty uzyskania przychodu (R16, R18). Umowa o pracę: 250 / 300 zł (warunki increased albo oświadczenie
--comment: increasedCosts), 0 przy employeeCostsWaiver, nie więcej niż dochód części opodatkowanej (art. 22 ust. 3b).
--comment: Zlecenie i dzieło: 20% albo 50% (author50 bez authorCostsWaiver; łącznie z zamkniętymi listami roku do
--comment: limitu kup.autorskie.limit — ponad limit bez kosztów [?]) od przychodu minus składki społeczne. Zwraca
--comment: koszty i kod pozycji (KUP albo KUP50).
CREATE OR REPLACE FUNCTION payroll.employee_costs(
    p_ctx payroll.ctx_t,
    p_income numeric,
    p_social numeric,
    p_kinds text[],
    p_author_costs_before numeric,
    OUT costs numeric,
    OUT code text,
    OUT trace jsonb
)
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_limit numeric;
BEGIN
    code := 'KUP';
    IF p_ctx.contract_kind = 'employment' THEN
        IF 'employeeCostsWaiver' = ANY (p_kinds) THEN
            costs := 0;
        ELSIF p_ctx.cost_kind = 'increased' OR 'increasedCosts' = ANY (p_kinds) THEN
            costs := platform.legal_parameter('kup.pracownik.podwyzszone.miesiac', p_ctx.payment_date);
        ELSE
            costs := platform.legal_parameter('kup.pracownik.miesiac', p_ctx.payment_date);
        END IF;
        costs := least(costs, greatest(p_income - p_social, 0));
        trace := jsonb_build_object('params', payroll.params(
            ARRAY['kup.pracownik.miesiac', 'kup.pracownik.podwyzszone.miesiac'], p_ctx.payment_date));
    ELSIF p_ctx.cost_kind = 'author50' AND NOT 'authorCostsWaiver' = ANY (p_kinds) THEN
        code := 'KUP50';
        v_limit := platform.legal_parameter('kup.autorskie.limit', p_ctx.payment_date);
        costs := least(round(greatest(p_income - p_social, 0) * platform.legal_parameter('kup.autorskie',
            p_ctx.payment_date), 2), greatest(v_limit - p_author_costs_before, 0));
        trace := jsonb_build_object('yearAuthorCostsBefore', p_author_costs_before,
            'params', payroll.params(ARRAY['kup.autorskie', 'kup.autorskie.limit'], p_ctx.payment_date));
    ELSE
        costs := round(greatest(p_income - p_social, 0) * platform.legal_parameter('kup.zlecenie', p_ctx.payment_date),
                       2);
        trace := jsonb_build_object('params', payroll.params(ARRAY['kup.zlecenie'], p_ctx.payment_date));
    END IF;
END;
$$;

--changeset hr:payroll.stage_tax runOnChange:true splitStatements:false
--comment: Etap 8 — zaliczka PIT (R16–R19) w miesiącu daty wypłaty. Przychód opodatkowany = przychody z cechą PIT +
--comment: wpłaty pracodawcy PPK (R19 [?]). Ulga dla młodych (do 26 lat w dniu wypłaty, bez youthReliefWaiver) i ulgi
--comment: 152–154 z oświadczeń — zwolnienie do limitu roku (zamknięte listy, art. 21 ust. 44) dla umowy o pracę
--comment: i zlecenia (ZWOL-MLODZI). Składki społeczne odliczane w części przypadającej na przychód opodatkowany.
--comment: Podstawa do zł. Umowa o pracę: 12% do progu 120 000 zł dochodu narastająco u płatnika, 32% nadwyżki;
--comment: higherRate — 32%; noAdvances — bez zaliczki do 30 000 zł dochodu u płatnika (art. 31c), od nadwyżki
--comment: w miesiącu przekroczenia [?]. Zlecenie i dzieło: 12% (art. 41 ust. 1; higherRate — 32%); ryczałt 12% od
--comment: przychodu (art. 30 ust. 1 pkt 5a), gdy kwota w umowie ≤ 200 zł i osoba nie ma umowy o pracę w spółce
--comment: (PIT-RYCZ). Kwota zmniejszająca z PIT-2 (300 × 12 ÷ mianownik udziału) raz w miesiącu podatkowym — na
--comment: pierwszej naliczonej liście z wypłatą w miesiącu [D] (PIT-ZMN). Zaliczka do zł, nie mniej niż 0.
--comment: Korekta (R4): pozycje są różnicami, koszty = p_ctx.costs_override.
CREATE OR REPLACE FUNCTION payroll.stage_tax(p_ctx payroll.ctx_t, p_lines payroll.line_t[])
    RETURNS payroll.line_t[]
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_lines payroll.line_t[] := p_lines;
    v_on date := p_ctx.payment_date;
    v_year integer := extract(year FROM p_ctx.payment_date)::integer;
    v_declarations record;
    v_totals record;
    v_income numeric;
    v_ppk_employer numeric;
    v_taxable numeric;
    v_social numeric;
    v_exempt numeric := 0;
    v_taxed numeric;
    v_social_deductible numeric;
    v_costs numeric;
    v_costs_code text;
    v_costs_trace jsonb := '{}';
    v_base numeric;
    v_base_for_advance numeric;
    v_threshold numeric;
    v_first numeric;
    v_rate1 numeric;
    v_rate2 numeric;
    v_tax numeric;
    v_reduction numeric := 0;
    v_advance numeric;
    v_limit numeric;
    v_trace jsonb;
BEGIN
    SELECT coalesce(array_agg(d.kind), '{}') AS kinds, coalesce(jsonb_agg(d.id), '[]') AS ids,
           max(d.share) FILTER (WHERE d.kind = 'pit2') AS share
    INTO v_declarations
    FROM tax_declarations.in_force(p_ctx.employee_id, v_on) d;
    SELECT * INTO v_totals FROM payroll.year_totals(p_ctx.employee_id, v_year, p_ctx.zus_exclude,
        p_ctx.pit_exclude);

    SELECT coalesce(sum(l.amount) FILTER (WHERE l.side = 'income' AND l.taxable), 0),
           coalesce(sum(l.amount) FILTER (WHERE l.code IN ('PPK-PR', 'PPK-PRD')), 0),
           coalesce(sum(l.amount) FILTER (WHERE l.code IN ('ZUS-E-P', 'ZUS-R-P', 'ZUS-C-P')), 0)
    INTO v_income, v_ppk_employer, v_social
    FROM unnest(v_lines) l;
    v_taxable := v_income + v_ppk_employer;
    v_rate1 := platform.legal_parameter('pit.skala.stawka1', v_on);
    v_rate2 := platform.legal_parameter('pit.skala.stawka2', v_on);
    v_trace := jsonb_build_object('declarationIds', v_declarations.ids, 'taxableIncome', v_taxable,
        'ppkEmployer', v_ppk_employer, 'yearTotalsBefore', to_jsonb(v_totals),
        'params', payroll.params(ARRAY['pit.skala.stawka1', 'pit.skala.stawka2', 'pit.skala.prog',
            'pit.kwota.zmniejszajaca.miesiac', 'pit.ulga.mlodzi.limit', 'pit.ryczalt.male.umowy.limit',
            'pit.niepobieranie.limit'], v_on));
    IF v_taxable = 0 AND p_ctx.mode = 'correction' AND p_ctx.costs_override = 0 THEN
        RETURN v_lines;
    END IF;

    IF p_ctx.contract_kind IN ('employment', 'mandate') AND v_taxable > 0
        AND ((payroll.age_on(p_ctx.birth_date, v_on) < 26 AND NOT 'youthReliefWaiver' = ANY (v_declarations.kinds))
             OR v_declarations.kinds && ARRAY['returnRelief', 'familyRelief', 'seniorRelief']) THEN
        v_limit := platform.legal_parameter('pit.ulga.mlodzi.limit', v_on);
        v_exempt := least(v_taxable, greatest(v_limit - v_totals.exempt_income, 0));
        v_lines := v_lines || payroll.item('ZWOL-MLODZI', 'info', v_taxable, NULL, v_exempt,
            jsonb_build_object('yearExemptBefore', v_totals.exempt_income, 'yearLimit', v_limit,
                               'declarationIds', v_declarations.ids));
    END IF;
    v_taxed := v_taxable - v_exempt;
    v_social_deductible := CASE WHEN v_exempt = 0 THEN v_social
                                WHEN v_taxable = 0 THEN 0
                                ELSE round(v_social * v_taxed / v_taxable, 2) END;

    IF p_ctx.contract_kind IN ('mandate', 'specific_work') AND v_taxed > 0 AND p_ctx.pay_basis <> 'hourly'
        AND p_ctx.base_amount <= platform.legal_parameter('pit.ryczalt.male.umowy.limit', v_on)
        AND NOT EXISTS (
            SELECT FROM employment.employments m
            WHERE m.employee_id = p_ctx.employee_id AND m.kind = 'employment'
              AND employment.period(m.start_date, m.end_date, m.terminated_at)
                  && daterange(p_ctx.period_from, p_ctx.period_to, '[]')
        ) THEN
        -- Ryczałt od przychodu [?] (spec R18: 12% od przychodu), bez kosztów i kwoty zmniejszającej.
        v_lines := v_lines || payroll.item('PIT-RYCZ', 'employee', v_taxed, v_rate1, round(v_taxed * v_rate1),
            v_trace);
        RETURN v_lines;
    END IF;

    IF p_ctx.mode = 'correction' THEN
        v_costs := p_ctx.costs_override;
        v_costs_code := CASE WHEN p_ctx.cost_kind = 'author50' THEN 'KUP50' ELSE 'KUP' END;
    ELSE
        SELECT c.costs, c.code, c.trace INTO v_costs, v_costs_code, v_costs_trace
        FROM payroll.employee_costs(p_ctx, v_taxed, v_social_deductible, v_declarations.kinds, v_totals.author_costs) c;
    END IF;
    IF v_costs <> 0 THEN
        v_lines := v_lines || payroll.item(v_costs_code, 'info', NULL, NULL, v_costs, v_costs_trace);
    END IF;

    v_base := round(v_taxed - v_social_deductible - v_costs);
    IF v_base < 0 THEN
        v_lines := v_lines || payroll.warning('negativeTaxBase', jsonb_build_object('base', v_base));
        v_base := 0;
    END IF;
    v_lines := v_lines || payroll.item('PODST-PIT', 'info', v_taxed, NULL, v_base,
        jsonb_build_object('socialDeductible', v_social_deductible, 'costs', v_costs, 'exempt', v_exempt));

    v_base_for_advance := v_base;
    IF 'noAdvances' = ANY (v_declarations.kinds) THEN
        v_limit := platform.legal_parameter('pit.niepobieranie.limit', v_on);
        v_base_for_advance := v_base - greatest(least(v_limit - v_totals.tax_base_all, v_base), 0);
        v_trace := v_trace || jsonb_build_object('noAdvancesFreeBase', v_base - v_base_for_advance);
    END IF;

    IF 'higherRate' = ANY (v_declarations.kinds) THEN
        v_tax := v_base_for_advance * v_rate2;
    ELSIF p_ctx.contract_kind = 'employment' THEN
        v_threshold := platform.legal_parameter('pit.skala.prog', v_on);
        v_first := greatest(least(v_base_for_advance, v_threshold - v_totals.tax_base_employment), 0);
        v_tax := v_first * v_rate1 + (v_base_for_advance - v_first) * v_rate2;
        v_trace := v_trace || jsonb_build_object('firstBracketBase', v_first,
                                                 'secondBracketBase', v_base_for_advance - v_first);
    ELSE
        v_tax := v_base_for_advance * v_rate1;
    END IF;

    IF v_declarations.share IS NOT NULL AND NOT EXISTS (
        SELECT FROM payroll.entries e
        JOIN payroll.payrolls p ON p.id = e.payroll_id
        JOIN payroll.lines l ON l.entry_id = e.id AND l.code = 'PIT-ZMN' AND l.amount > 0
        WHERE e.employee_id = p_ctx.employee_id
          AND date_trunc('month', p.payment_date) = date_trunc('month', v_on)
          AND (p.id = p_ctx.payroll_id OR (p.status IN ('calculated', 'closed') AND NOT p.id = ANY (p_ctx.pit_exclude)))
    ) THEN
        v_reduction := round(platform.legal_parameter('pit.kwota.zmniejszajaca.miesiac', v_on) * 12
            / split_part(v_declarations.share, '/', 2)::numeric, 2);
        v_lines := v_lines || payroll.item('PIT-ZMN', 'info', NULL, NULL, v_reduction,
            jsonb_build_object('share', v_declarations.share, 'declarationIds', v_declarations.ids));
    END IF;

    v_advance := greatest(round(v_tax - v_reduction), 0);
    v_lines := v_lines || payroll.item('PIT', 'employee', v_base_for_advance,
        CASE WHEN v_base_for_advance > 0 THEN round(v_tax / v_base_for_advance, 6) END, v_advance,
        v_trace || jsonb_build_object('reduction', v_reduction));
    RETURN v_lines;
END;
$$;

--changeset hr:payroll.stage_deductions runOnChange:true splitStatements:false
--comment: Etap 10 — potrącenia (R22, R23) z netto (R21) w kolejności KP art. 87 § 1: zajęcia alimentacyjne (do 3/5,
--comment: bez kwoty wolnej), egzekucje inne (do 1/2, kwota wolna 100%), zaliczki pieniężne (1/2 łącznie z egzekucją,
--comment: wolna 75%), kary (1/10 netto po potrąceniach 1–3, wolna 90%), dobrowolne (art. 91: na rzecz pracodawcy
--comment: wolna 100%, innych 80%) w kwocie składnika. Potrącenia 1–3 razem do 1/2, przy alimentach do 3/5 (§ 4).
--comment: Kwota wolna = płaca minimalna netto (parametr z PPK, gdy pracownik wpłaca do PPK) × etat (art. 87¹ § 2);
--comment: zlecenie i dzieło — bez etatu [D: KPC art. 833 § 2¹ stosowany do każdej umowy]. Zajęcia aktywne, doręczone
--comment: najpóźniej w dniu wypłaty, alimenty przed innymi, potem data doręczenia; nie więcej niż pozostało do
--comment: ściągnięcia (zamknięte listy). Granice obcinane do grosza w dół.
CREATE OR REPLACE FUNCTION payroll.stage_deductions(p_ctx payroll.ctx_t, p_lines payroll.line_t[])
    RETURNS payroll.line_t[]
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_lines payroll.line_t[] := p_lines;
    v_on date := p_ctx.payment_date;
    v_net numeric;
    v_fte numeric := CASE WHEN p_ctx.contract_kind = 'employment' THEN p_ctx.fte ELSE 1 END;
    v_free numeric;
    v_alimony numeric := 0;
    v_other numeric := 0;
    v_advances numeric := 0;
    v_penalties numeric := 0;
    v_total numeric := 0;
    v_group_limit numeric;
    v_item record;
    v_requested numeric;
    v_amount numeric;
    v_collected numeric;
    v_free_code text;
BEGIN
    v_net := payroll.sum_lines(v_lines, '{}', 'income') - payroll.sum_lines(v_lines, '{}', 'employee');
    IF v_net <= 0 THEN
        RETURN v_lines;
    END IF;
    v_free_code := CASE WHEN payroll.sum_lines(v_lines, ARRAY['PPK-P']) > 0
                        THEN 'potracenia.kwota.wolna.100.ppk' ELSE 'potracenia.kwota.wolna.100' END;

    FOR v_item IN
        SELECT g.id, g.kind, g.monthly_amount, g.total_amount, g.received_at
        FROM pay_components.garnishments g
        WHERE g.employee_id = p_ctx.employee_id AND g.status = 'active' AND g.received_at <= v_on
        ORDER BY g.kind <> 'alimony', g.received_at, g.case_number
    LOOP
        v_free := coalesce(v_free, round(platform.legal_parameter(v_free_code, v_on) * v_fte, 2));
        v_collected := payroll.garnishment_collected(v_item.id);
        v_requested := CASE
            WHEN v_item.total_amount IS NULL THEN v_item.monthly_amount
            WHEN v_item.monthly_amount IS NULL THEN v_item.total_amount - v_collected
            ELSE least(v_item.monthly_amount, v_item.total_amount - v_collected)
        END;
        IF v_requested <= 0 THEN
            CONTINUE;
        END IF;
        IF v_item.kind = 'alimony' THEN
            v_amount := least(v_requested, trunc(v_net * 3 / 5, 2) - v_alimony, v_net - v_total);
        ELSE
            v_group_limit := CASE WHEN v_alimony > 0 THEN trunc(v_net * 3 / 5, 2) ELSE trunc(v_net / 2, 2) END;
            v_amount := least(v_requested, trunc(v_net / 2, 2) - v_other,
                              v_group_limit - v_alimony - v_other, v_net - v_total - v_free);
        END IF;
        v_amount := greatest(v_amount, 0);
        IF v_amount < v_requested THEN
            v_lines := v_lines || payroll.warning('deductionLimited',
                jsonb_build_object('garnishmentId', v_item.id, 'requested', v_requested, 'deducted', v_amount));
        END IF;
        IF v_amount > 0 THEN
            v_lines := v_lines || payroll.item(CASE WHEN v_item.kind = 'alimony' THEN 'ALIM' ELSE 'KOMOR' END,
                'deduction', v_requested, NULL, v_amount,
                jsonb_build_object('garnishmentId', v_item.id, 'collectedBefore', v_collected, 'net', v_net,
                                   'freeAmount', CASE WHEN v_item.kind = 'alimony' THEN NULL ELSE v_free END,
                                   'params', payroll.params(ARRAY[v_free_code], v_on)),
                (SELECT t.id FROM pay_components.component_types t
                 WHERE t.code = CASE WHEN v_item.kind = 'alimony' THEN 'ALIM' ELSE 'KOMOR' END));
            IF v_item.kind = 'alimony' THEN
                v_alimony := v_alimony + v_amount;
            ELSE
                v_other := v_other + v_amount;
            END IF;
            v_total := v_total + v_amount;
        END IF;
    END LOOP;

    FOR v_item IN
        SELECT c.id, c.value, c.consent_id, t.id AS type_id, t.code, t.deduction_category
        FROM pay_components.employment_components c
        JOIN pay_components.component_types t ON t.id = c.component_type_id
        WHERE c.employment_id = p_ctx.employment_id AND t.kind = 'deduction' AND NOT t.is_system
          AND daterange(c.valid_from, c.valid_to, '[]') && daterange(p_ctx.from_date, p_ctx.to_date, '[]')
        ORDER BY array_position(ARRAY['advance', 'penalty', 'voluntaryEmployer', 'voluntaryOther'],
                                t.deduction_category), t.code, c.valid_from
    LOOP
        v_free := coalesce(v_free, round(platform.legal_parameter(v_free_code, v_on) * v_fte, 2));
        v_amount := CASE v_item.deduction_category
            WHEN 'advance' THEN least(v_item.value, trunc(v_net / 2, 2) - v_other - v_advances,
                CASE WHEN v_alimony > 0 THEN trunc(v_net * 3 / 5, 2) ELSE trunc(v_net / 2, 2) END
                    - v_alimony - v_other - v_advances,
                v_net - v_total - round(v_free * 0.75, 2))
            WHEN 'penalty' THEN least(v_item.value, trunc((v_net - v_alimony - v_other - v_advances) / 10, 2)
                - v_penalties, v_net - v_total - round(v_free * 0.9, 2))
            WHEN 'voluntaryEmployer' THEN least(v_item.value, v_net - v_total - v_free)
            ELSE least(v_item.value, v_net - v_total - round(v_free * 0.8, 2))
        END;
        v_amount := greatest(v_amount, 0);
        IF v_amount < v_item.value THEN
            v_lines := v_lines || payroll.warning('deductionLimited',
                jsonb_build_object('employmentComponentId', v_item.id, 'requested', v_item.value,
                                   'deducted', v_amount));
        END IF;
        IF v_amount > 0 THEN
            v_lines := v_lines || payroll.item(v_item.code, 'deduction', v_item.value, NULL, v_amount,
                jsonb_build_object('employmentComponentId', v_item.id, 'consentId', v_item.consent_id,
                                   'category', v_item.deduction_category, 'net', v_net, 'freeAmount', v_free,
                                   'params', payroll.params(ARRAY[v_free_code], v_on)),
                v_item.type_id);
            v_total := v_total + v_amount;
            IF v_item.deduction_category = 'advance' THEN
                v_advances := v_advances + v_amount;
            ELSIF v_item.deduction_category = 'penalty' THEN
                v_penalties := v_penalties + v_amount;
            END IF;
        END IF;
    END LOOP;
    RETURN v_lines;
END;
$$;
