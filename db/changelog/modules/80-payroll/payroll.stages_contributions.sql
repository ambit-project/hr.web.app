--liquibase formatted sql

--changeset hr:payroll.stage_social runOnChange:true splitStatements:false
--comment: Etap 5 — składki społeczne (R13, R14). Podstawa = przychody z cechą ZUS (bez CHOR i zasiłku). Emerytalna
--comment: i rentowe do limitu 30-krotności w roku (suma podstaw zamkniętych list kartoteki po okresie), chorobowa
--comment: (pracownik) i wypadkowa (stopa spółki) od całej podstawy — według ubezpieczeń z warunków umowy (004 R12).
--comment: FP (FP + FS łącznie) od podstawy ≥ płaca minimalna w miesiącu, FGŚP bez progu; oba nie za kobiety od 55
--comment: i mężczyzn od 60 lat — od miesiąca po miesiącu ukończenia wieku [D]; płeć z PESEL, bez PESEL płaci się
--comment: z ostrzeżeniem peselMissing [D]. Informacyjnie PODST-ZUS (podstawa bez limitu) i PODST-CHOR (przychody
--comment: w podstawie chorobowego — do podstawy chorobowego kolejnych miesięcy, R7).
CREATE OR REPLACE FUNCTION payroll.stage_social(p_ctx payroll.ctx_t, p_lines payroll.line_t[])
    RETURNS payroll.line_t[]
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_lines payroll.line_t[] := p_lines;
    v_on date := p_ctx.period_from;
    v_base numeric;
    v_sickness_base numeric;
    v_totals record;
    v_limit numeric;
    v_limited numeric;
    v_settings record;
    v_male boolean;
    v_exempt boolean := false;
    v_rate numeric;
    v_trace jsonb;
BEGIN
    IF p_ctx.contract_kind = 'specific_work' OR NOT p_ctx.insurances && ARRAY['pension', 'disability', 'sickness',
        'accident', 'fp', 'fgsp'] THEN
        RETURN v_lines;
    END IF;
    SELECT coalesce(sum(l.amount) FILTER (WHERE l.in_zus_base), 0),
           coalesce(sum(l.amount) FILTER (WHERE l.in_sickness_base), 0)
    INTO v_base, v_sickness_base
    FROM unnest(v_lines) l
    WHERE l.side = 'income';

    SELECT * INTO v_totals FROM payroll.year_totals(p_ctx.employee_id, extract(year FROM v_on)::integer,
        p_ctx.zus_exclude, p_ctx.pit_exclude, p_ctx.period_from);
    v_limit := platform.legal_parameter('zus.limit.roczny', v_on);
    v_limited := greatest(least(v_base, v_limit - v_totals.pension_base), 0);
    v_trace := jsonb_build_object('yearPensionBaseBefore', v_totals.pension_base, 'yearLimit', v_limit,
        'params', payroll.params(ARRAY['zus.emerytalna.pracownik', 'zus.emerytalna.pracodawca',
            'zus.rentowa.pracownik', 'zus.rentowa.pracodawca', 'zus.chorobowa.pracownik', 'zus.limit.roczny'], v_on));

    v_lines := v_lines || payroll.item('PODST-ZUS', 'info', NULL, NULL, v_base, v_trace)
        || payroll.item('PODST-CHOR', 'info', NULL, NULL, v_sickness_base, '{}');
    IF v_limited < v_base THEN
        v_lines := v_lines || payroll.warning('pensionLimitReached', jsonb_build_object('limitedBase', v_limited));
    END IF;

    IF 'pension' = ANY (p_ctx.insurances) THEN
        v_rate := platform.legal_parameter('zus.emerytalna.pracownik', v_on);
        v_lines := v_lines || payroll.item('ZUS-E-P', 'employee', v_limited, v_rate, round(v_limited * v_rate, 2),
            v_trace);
        v_rate := platform.legal_parameter('zus.emerytalna.pracodawca', v_on);
        v_lines := v_lines || payroll.item('ZUS-E-PR', 'employer', v_limited, v_rate, round(v_limited * v_rate, 2),
            v_trace);
    END IF;
    IF 'disability' = ANY (p_ctx.insurances) THEN
        v_rate := platform.legal_parameter('zus.rentowa.pracownik', v_on);
        v_lines := v_lines || payroll.item('ZUS-R-P', 'employee', v_limited, v_rate, round(v_limited * v_rate, 2),
            v_trace);
        v_rate := platform.legal_parameter('zus.rentowa.pracodawca', v_on);
        v_lines := v_lines || payroll.item('ZUS-R-PR', 'employer', v_limited, v_rate, round(v_limited * v_rate, 2),
            v_trace);
    END IF;
    IF 'sickness' = ANY (p_ctx.insurances) THEN
        v_rate := platform.legal_parameter('zus.chorobowa.pracownik', v_on);
        v_lines := v_lines || payroll.item('ZUS-C-P', 'employee', v_base, v_rate, round(v_base * v_rate, 2), v_trace);
    END IF;
    IF 'accident' = ANY (p_ctx.insurances) THEN
        SELECT * INTO v_settings FROM payroll.settings_on(p_ctx.company_id, v_on);
        v_lines := v_lines || payroll.item('ZUS-W-PR', 'employer', v_base, v_settings.accident_rate,
            round(v_base * v_settings.accident_rate, 2),
            jsonb_build_object('settingsId', v_settings.settings_id,
                               'params', payroll.params(ARRAY['zus.wypadkowa.domyslna'], v_on)));
    END IF;

    IF p_ctx.insurances && ARRAY['fp', 'fgsp'] THEN
        v_male := payroll.is_male(p_ctx.employee_id);
        IF v_male IS NULL THEN
            v_lines := v_lines || payroll.warning('peselMissing', '{}');
        ELSE
            -- Ustawa o rynku pracy art. 261, ustawa o FGŚP art. 9b: od miesiąca po ukończeniu 55 / 60 lat.
            v_exempt := (p_ctx.birth_date + make_interval(years => CASE WHEN v_male THEN 60 ELSE 55 END))::date
                < p_ctx.period_from;
        END IF;
        IF 'fp' = ANY (p_ctx.insurances) AND NOT v_exempt
            AND v_base >= platform.legal_parameter('placa.minimalna', v_on) THEN
            v_rate := platform.legal_parameter('fp.pracodawca', v_on);
            v_lines := v_lines || payroll.item('FP', 'employer', v_base, v_rate, round(v_base * v_rate, 2),
                jsonb_build_object('params', payroll.params(ARRAY['fp.pracodawca', 'placa.minimalna'], v_on)));
        END IF;
        IF 'fgsp' = ANY (p_ctx.insurances) AND NOT v_exempt THEN
            v_rate := platform.legal_parameter('fgsp.pracodawca', v_on);
            v_lines := v_lines || payroll.item('FGSP', 'employer', v_base, v_rate, round(v_base * v_rate, 2),
                jsonb_build_object('params', payroll.params(ARRAY['fgsp.pracodawca'], v_on)));
        END IF;
    END IF;
    RETURN v_lines;
END;
$$;

--changeset hr:payroll.stage_ppk runOnChange:true splitStatements:false
--comment: Etap 6 — PPK (R20): stawki z 008 ppk.rates_on w dniu wypłaty (korekta — wypłaty listy korygowanej), podstawa =
--comment: podstawa składek bez limitu 30-krotności. Obniżona wpłata podstawowa pracownika pomijana (stawka z parametru
--comment: ppk.pracownik.podstawowa), gdy podstawa > ppk.prog.obnizenia (art. 27 ust. 5). PPK-P i PPK-PD zmniejszają
--comment: netto, PPK-PR i PPK-PRD — koszt pracodawcy i przychód pracownika do PIT (R19).
CREATE OR REPLACE FUNCTION payroll.stage_ppk(p_ctx payroll.ctx_t, p_lines payroll.line_t[])
    RETURNS payroll.line_t[]
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_lines payroll.line_t[] := p_lines;
    v_rates record;
    v_base numeric;
    v_basic numeric;
    v_trace jsonb;
BEGIN
    IF p_ctx.contract_kind = 'specific_work' THEN
        RETURN v_lines;
    END IF;
    SELECT * INTO v_rates FROM ppk.rates_on(p_ctx.employee_id, p_ctx.ppk_date);
    IF v_rates.employee_basic_rate + v_rates.employee_additional_rate + v_rates.employer_basic_rate
        + v_rates.employer_additional_rate = 0 THEN
        RETURN v_lines;
    END IF;
    v_base := payroll.sum_lines(v_lines, ARRAY['PODST-ZUS'], 'info');
    v_basic := v_rates.employee_basic_rate;
    v_trace := jsonb_build_object('ppkDate', p_ctx.ppk_date, 'rates', to_jsonb(v_rates),
        'params', payroll.params(ARRAY['ppk.prog.obnizenia', 'ppk.pracownik.podstawowa'], p_ctx.ppk_date));
    IF v_basic < platform.legal_parameter('ppk.pracownik.podstawowa', p_ctx.ppk_date) * 100
        AND v_base > platform.legal_parameter('ppk.prog.obnizenia', p_ctx.ppk_date) THEN
        v_basic := platform.legal_parameter('ppk.pracownik.podstawowa', p_ctx.ppk_date) * 100;
        v_trace := v_trace || '{"reducedRateIgnored": true}';
    END IF;
    IF v_basic > 0 THEN
        v_lines := v_lines || payroll.item('PPK-P', 'employee', v_base, v_basic / 100, round(v_base * v_basic / 100, 2),
            v_trace);
    END IF;
    IF v_rates.employee_additional_rate > 0 THEN
        v_lines := v_lines || payroll.item('PPK-PD', 'employee', v_base, v_rates.employee_additional_rate / 100,
            round(v_base * v_rates.employee_additional_rate / 100, 2), v_trace);
    END IF;
    IF v_rates.employer_basic_rate > 0 THEN
        v_lines := v_lines || payroll.item('PPK-PR', 'employer', v_base, v_rates.employer_basic_rate / 100,
            round(v_base * v_rates.employer_basic_rate / 100, 2), v_trace);
    END IF;
    IF v_rates.employer_additional_rate > 0 THEN
        v_lines := v_lines || payroll.item('PPK-PRD', 'employer', v_base, v_rates.employer_additional_rate / 100,
            round(v_base * v_rates.employer_additional_rate / 100, 2), v_trace);
    END IF;
    RETURN v_lines;
END;
$$;

--changeset hr:payroll.stage_health runOnChange:true splitStatements:false
--comment: Etap 7 — składka zdrowotna 9% (R15; ustawa o świadczeniach art. 81 ust. 1 i 6): podstawa = przychody z cechą
--comment: zdrowotnej (zasadnicze, składniki, wynagrodzenie chorobowe; bez zasiłku) − składki społeczne pracownika.
--comment: Ograniczenie do zaliczki liczonej po zasadach z 2021 r. (art. 83) — nie liczone [?].
CREATE OR REPLACE FUNCTION payroll.stage_health(p_ctx payroll.ctx_t, p_lines payroll.line_t[])
    RETURNS payroll.line_t[]
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_lines payroll.line_t[] := p_lines;
    v_base numeric;
    v_rate numeric;
BEGIN
    IF NOT 'health' = ANY (p_ctx.insurances) THEN
        RETURN v_lines;
    END IF;
    SELECT coalesce(sum(l.amount) FILTER (WHERE l.side = 'income' AND l.in_health_base), 0)
        - coalesce(sum(l.amount) FILTER (WHERE l.code IN ('ZUS-E-P', 'ZUS-R-P', 'ZUS-C-P')), 0)
    INTO v_base
    FROM unnest(v_lines) l;
    v_base := greatest(v_base, 0);
    v_rate := platform.legal_parameter('zdrowotna.stawka', p_ctx.period_from);
    v_lines := v_lines || payroll.item('ZDROW', 'employee', v_base, v_rate, round(v_base * v_rate, 2),
        jsonb_build_object('params', payroll.params(ARRAY['zdrowotna.stawka'], p_ctx.period_from)));
    RETURN v_lines;
END;
$$;
