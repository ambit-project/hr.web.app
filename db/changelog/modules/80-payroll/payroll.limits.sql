--liquibase formatted sql

--changeset hr:payroll.blocking_warnings runOnChange:true splitStatements:false
--comment: Ostrzeżenia, przy których listy nie można zamknąć (zasada „czego nie wiemy, tego nie liczymy”): przypadki, których
--comment: reguła nie została potwierdzona w przepisach albo zależy od danych spoza systemu. Kwoty nie są liczone; Kadry
--comment: usuwają przyczynę (np. wyłączają umowę z listy) i rozliczają przypadek poza systemem.
CREATE OR REPLACE FUNCTION payroll.blocking_warnings()
    RETURNS text[]
    LANGUAGE sql
    IMMUTABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT ARRAY['sicknessBaseMissing', 'sicknessBaseUnknown', 'sicknessBaseSupplement', 'sicknessBaseFteChange',
                 'sicknessWaitingPeriod', 'overtimeNormalPayUnknown', 'equivalentComponentsUnknown',
                 'healthCapUnknown', 'authorCostsLimitReached', 'noAdvancesThresholdCrossed'];
$$;

--changeset hr:payroll.ppk_income_sources runOnChange:true splitStatements:false
--comment: Wpłaty pracodawcy PPK umowy, które są przychodem do opodatkowania na liście z wypłatą w dniu p_on (R19):
--comment: z zamkniętych list przekazane do instytucji finansowej (ppk_paid_on) w miesiącu p_on, nie później niż p_on,
--comment: nie ujęte jeszcze na innej naliczonej albo zamkniętej liście (PPK-PRZ ze śladem sourceEntryIds). Wpłata
--comment: przekazana bez późniejszej wypłaty w tym miesiącu nie trafia na listę — wchodzi do PIT-11 (010 R6).
CREATE OR REPLACE FUNCTION payroll.ppk_income_sources(p_employment_id uuid, p_payroll_id uuid, p_on date)
    RETURNS TABLE (entry_id uuid, payroll_id uuid, paid_on date, amount numeric)
    LANGUAGE sql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT e.id, p.id, p.ppk_paid_on, sum(l.amount)
    FROM payroll.entries e
    JOIN payroll.payrolls p ON p.id = e.payroll_id AND p.status = 'closed' AND p.ppk_paid_on IS NOT NULL
    JOIN payroll.lines l ON l.entry_id = e.id AND l.code IN ('PPK-PR', 'PPK-PRD')
    WHERE e.employment_id = p_employment_id
      AND date_trunc('month', p.ppk_paid_on) = date_trunc('month', p_on) AND p.ppk_paid_on <= p_on
      AND NOT EXISTS (
          SELECT FROM payroll.lines x
          JOIN payroll.entries xe ON xe.id = x.entry_id
          JOIN payroll.payrolls xp ON xp.id = xe.payroll_id AND xp.status IN ('calculated', 'closed')
          WHERE x.code = 'PPK-PRZ' AND x.trace -> 'sourceEntryIds' ? e.id::text AND xp.id <> p_payroll_id
      )
    GROUP BY e.id, p.id;
$$;

--changeset hr:payroll.ppk_taxed_entries runOnChange:true splitStatements:false
--comment: Wypłaty, których wpłaty pracodawcy PPK zostały ujęte jako przychód (PPK-PRZ) na listach o podanych statusach.
CREATE OR REPLACE FUNCTION payroll.ppk_taxed_entries(p_statuses text[])
    RETURNS TABLE (entry_id uuid, taxed_on_payroll_id uuid)
    LANGUAGE sql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT s.value::uuid, p.id
    FROM payroll.lines l
    JOIN payroll.entries e ON e.id = l.entry_id
    JOIN payroll.payrolls p ON p.id = e.payroll_id AND p.status = ANY (p_statuses)
    CROSS JOIN LATERAL jsonb_array_elements_text(l.trace -> 'sourceEntryIds') s
    WHERE l.code = 'PPK-PRZ';
$$;

--changeset hr:payroll.health_cap runOnChange:true splitStatements:false
--comment: Obniżenie składki zdrowotnej do zaliczki liczonej według ustawy o PIT w brzmieniu z 31.12.2021 (R15; ustawa
--comment: o świadczeniach art. 83 ust. 1, 2a, 2b, 3 pkt 6). Zaliczka 2021 (ustawa o PIT t.j. Dz.U. 2021 poz. 1128) =
--comment: 17% podstawy (przychód razem z przychodem zwolnionym i wpłatą PPK − składki społeczne − koszty), do zł; umowa
--comment: o pracę: minus 43,76 zł (art. 32 ust. 3: 1/12 kwoty zmniejszającej) przy PIT-2, 17% do 85 528 zł dochodu
--comment: w roku (art. 32 ust. 1); zlecenie: bez kwoty zmniejszającej (art. 41 ust. 1). Bez odliczenia 7,75% składki
--comment: (stanowisko MZ; z odliczeniem obniżenie dotyczyłoby prawie każdej pensji). Składka nie większa od 17% podstawy
--comment: (umowa o pracę: minus 43,76 zł) — bez zmian (obniżenie na pewno nie zachodzi). Inaczej obniżona do zaliczki tylko w przypadku
--comment: ustalonym: lista zwykła, umowa o pracę albo zlecenie bez kosztów 50%, PIT-2 z 1/12 albo bez PIT-2 (umowa
--comment: o pracę), jedna lista z wypłatą w miesiącu, dochód roku w pierwszym przedziale 2021; w pozostałych (PIT-2 1/24
--comment: i 1/36, kilka wypłat w miesiącu, korekta, koszty 50%) — ostrzeżenie blokujące healthCapUnknown.
--comment: Ryczałt (PIT-RYCZ) — bez obniżenia (ust. 3 pkt 6).
CREATE OR REPLACE FUNCTION payroll.health_cap(p_ctx payroll.ctx_t, p_lines payroll.line_t[])
    RETURNS payroll.line_t[]
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_lines payroll.line_t[] := '{}';
    v_line payroll.line_t;
    v_health numeric := payroll.sum_lines(p_lines, ARRAY['ZDROW']);
    v_income numeric;
    v_social numeric;
    v_kinds text[];
    v_share text;
    v_costs numeric;
    v_base numeric;
    v_tax numeric;
    v_reduction numeric;
    v_advance numeric;
    v_totals record;
    v_exact boolean;
    v_trace jsonb;
BEGIN
    IF v_health <= 0 OR EXISTS (SELECT FROM unnest(p_lines) l WHERE l.code = 'PIT-RYCZ') THEN
        RETURN p_lines;
    END IF;
    SELECT coalesce(array_agg(d.kind), '{}'), max(d.share) FILTER (WHERE d.kind = 'pit2')
    INTO v_kinds, v_share
    FROM tax_declarations.in_force(p_ctx.employee_id, p_ctx.payment_date) d;
    v_income := payroll.sum_lines(ARRAY(SELECT l FROM unnest(p_lines) l WHERE l.side = 'income' AND l.taxable), '{}')
        + payroll.sum_lines(p_lines, ARRAY['PPK-PRZ']);
    v_social := payroll.sum_lines(p_lines, ARRAY['ZUS-E-P', 'ZUS-R-P', 'ZUS-C-P']);
    SELECT c.costs INTO v_costs FROM payroll.employee_costs(p_ctx, v_income, v_social, v_kinds, 0) c;
    v_base := greatest(round(v_income - v_social - v_costs), 0);
    v_tax := round(v_base * 0.17, 2);
    v_trace := jsonb_build_object('base2021', v_base, 'tax2021', v_tax, 'health', v_health);
    -- Na pewno bez obniżenia: składka nie większa od zaliczki 2021 z największą możliwą kwotą zmniejszającą.
    IF v_health <= round(v_tax - CASE WHEN p_ctx.contract_kind = 'employment' THEN 43.76 ELSE 0 END) THEN
        RETURN p_lines;
    END IF;

    SELECT * INTO v_totals FROM payroll.year_totals(p_ctx.employee_id, extract(year FROM p_ctx.payment_date)::integer,
        p_ctx.zus_exclude, p_ctx.pit_exclude);
    v_exact := p_ctx.mode = 'regular' AND p_ctx.contract_kind IN ('employment', 'mandate')
        AND coalesce(p_ctx.cost_kind, '') <> 'author50'
        AND (p_ctx.contract_kind = 'mandate' OR (coalesce(v_share, '1/12') = '1/12'
             AND v_totals.tax_base_employment + v_totals.exempt_income + v_base <= 85528))
        AND NOT EXISTS (
            SELECT FROM payroll.entries e
            JOIN payroll.payrolls p ON p.id = e.payroll_id
            WHERE e.employee_id = p_ctx.employee_id AND p.id <> p_ctx.payroll_id
              AND p.status IN ('calculated', 'closed')
              AND date_trunc('month', p.payment_date) = date_trunc('month', p_ctx.payment_date)
        );
    IF NOT v_exact THEN
        RETURN p_lines || payroll.warning('healthCapUnknown', v_trace);
    END IF;
    v_reduction := CASE WHEN p_ctx.contract_kind = 'employment' AND v_share IS NOT NULL THEN 43.76 ELSE 0 END;
    v_advance := greatest(round(v_tax - v_reduction), 0);
    IF v_health <= v_advance THEN
        RETURN p_lines;
    END IF;
    FOREACH v_line IN ARRAY p_lines LOOP
        IF v_line.code = 'ZDROW' THEN
            v_line.amount := v_advance;
            v_line.trace := v_line.trace || v_trace || jsonb_build_object('cappedFrom', v_health,
                'advance2021', v_advance, 'reduction2021', v_reduction);
        END IF;
        v_lines := v_lines || v_line;
    END LOOP;
    RETURN v_lines;
END;
$$;
