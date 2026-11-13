--liquibase formatted sql

--changeset hr:payroll.payroll_lock runOnChange:true splitStatements:false
--comment: Lista płac do zapisu: U0002, gdy nie ma; blokada wiersza i wersja (xmin; NULL — bez sprawdzania).
CREATE OR REPLACE FUNCTION payroll.payroll_lock(p_id uuid, p_row_version bigint)
    RETURNS payroll.payrolls
    LANGUAGE plpgsql
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_row payroll.payrolls%ROWTYPE;
BEGIN
    SELECT * INTO v_row FROM payroll.payrolls p WHERE p.id = p_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'payroll not found' USING ERRCODE = 'U0002';
    END IF;
    IF p_row_version IS NOT NULL
        AND (SELECT p.xmin::text::bigint FROM payroll.payrolls p WHERE p.id = p_id) <> p_row_version THEN
        RAISE EXCEPTION 'payroll changed' USING ERRCODE = 'U0003';
    END IF;
    RETURN v_row;
END;
$$;

--changeset hr:payroll_app.payroll_list runOnChange:true splitStatements:false
--comment: Listy płac spółki z okresem w roku (R1, R2; Kadry, Administrator), od najnowszego okresu: numer, rodzaj,
--comment: okres, data wypłaty (payment_date_late — po 10. dniu następnego miesiąca, KP art. 85 § 2 [D: ostrzeżenie]),
--comment: lista korygowana, status, nieaktualność, liczba wypłat i sumy.
DROP FUNCTION IF EXISTS payroll_app.payroll_list(uuid, integer);
CREATE FUNCTION payroll_app.payroll_list(p_company_id uuid, p_year integer)
    RETURNS TABLE (
        id uuid,
        number text,
        kind text,
        period_from date,
        period_to date,
        payment_date date,
        payment_date_late boolean,
        corrects_payroll_id uuid,
        corrects_number text,
        status text,
        is_stale boolean,
        entry_count integer,
        gross numeric,
        net numeric,
        payout numeric,
        row_version bigint
    )
    LANGUAGE plpgsql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    IF NOT EXISTS (SELECT FROM platform.companies c WHERE c.id = p_company_id) THEN
        RAISE EXCEPTION 'company not found' USING ERRCODE = 'U0002';
    END IF;
    RETURN QUERY
    SELECT p.id, p.number, p.kind, p.period_from, p.period_to, p.payment_date,
           p.payment_date > (p.period_from + interval '1 month 9 days')::date,
           p.corrects_payroll_id, c.number, p.status, p.is_stale,
           (SELECT count(*)::integer FROM payroll.entries e WHERE e.payroll_id = p.id),
           coalesce((SELECT sum(e.gross) FROM payroll.entries e WHERE e.payroll_id = p.id), 0),
           coalesce((SELECT sum(e.net) FROM payroll.entries e WHERE e.payroll_id = p.id), 0),
           coalesce((SELECT sum(e.payout) FROM payroll.entries e WHERE e.payroll_id = p.id), 0),
           p.xmin::text::bigint
    FROM payroll.payrolls p
    LEFT JOIN payroll.payrolls c ON c.id = p.corrects_payroll_id
    WHERE p.company_id = p_company_id AND extract(year FROM p.period_from) = p_year
    ORDER BY p.period_from DESC, p.kind = 'correction', p.number;
END;
$$;

--changeset hr:payroll_app.payroll_get runOnChange:true splitStatements:false
--comment: Lista płac z podsumowaniem (R2, R24): sumy przychodu, składek pracownika i pracodawcy, zdrowotnej, PIT, PPK,
--comment: netto, potrąceń, do wypłaty, liczba wypłat z ostrzeżeniami i z ostrzeżeniami blokującymi zamknięcie
--comment: (payroll.blocking_warnings), data przekazania wpłat PPK (R19). Bez kwot osób — bez audytu odczytu.
DROP FUNCTION IF EXISTS payroll_app.payroll_get(uuid);
CREATE FUNCTION payroll_app.payroll_get(p_id uuid)
    RETURNS TABLE (
        id uuid,
        company_id uuid,
        number text,
        kind text,
        period_from date,
        period_to date,
        payment_date date,
        payment_date_late boolean,
        corrects_payroll_id uuid,
        corrects_number text,
        status text,
        is_stale boolean,
        calculated_at timestamptz,
        closed_at timestamptz,
        ppk_paid_on date,
        entry_count integer,
        warning_count integer,
        blocked_count integer,
        member_count integer,
        excluded_count integer,
        gross numeric,
        employee_contributions numeric,
        health numeric,
        pit numeric,
        ppk_employee numeric,
        net numeric,
        deductions numeric,
        payout numeric,
        employer_cost numeric,
        row_version bigint
    )
    LANGUAGE plpgsql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    IF NOT EXISTS (SELECT FROM payroll.payrolls p WHERE p.id = p_id) THEN
        RAISE EXCEPTION 'payroll not found' USING ERRCODE = 'U0002';
    END IF;
    RETURN QUERY
    WITH sums AS (
        SELECT coalesce(sum(l.amount) FILTER (WHERE l.code IN ('ZUS-E-P', 'ZUS-R-P', 'ZUS-C-P')), 0) AS contributions,
               coalesce(sum(l.amount) FILTER (WHERE l.code = 'ZDROW'), 0) AS health,
               coalesce(sum(l.amount) FILTER (WHERE l.code IN ('PIT', 'PIT-RYCZ')), 0) AS pit,
               coalesce(sum(l.amount) FILTER (WHERE l.code IN ('PPK-P', 'PPK-PD')), 0) AS ppk,
               coalesce(sum(l.amount) FILTER (WHERE l.side = 'deduction'), 0) AS deductions,
               coalesce(sum(l.amount) FILTER (WHERE l.side = 'employer'), 0) AS employer
        FROM payroll.entries e
        JOIN payroll.lines l ON l.entry_id = e.id
        WHERE e.payroll_id = p_id
    )
    SELECT p.id, p.company_id, p.number, p.kind, p.period_from, p.period_to, p.payment_date,
           p.payment_date > (p.period_from + interval '1 month 9 days')::date,
           p.corrects_payroll_id, c.number, p.status, p.is_stale, p.calculated_at, p.closed_at, p.ppk_paid_on,
           (SELECT count(*)::integer FROM payroll.entries e WHERE e.payroll_id = p.id),
           (SELECT count(*)::integer FROM payroll.entries e WHERE e.payroll_id = p.id AND e.warnings <> '[]'),
           (SELECT count(*)::integer FROM payroll.entries e WHERE e.payroll_id = p.id AND EXISTS (
                SELECT FROM jsonb_array_elements(e.warnings) w
                WHERE w ->> 'code' = ANY (payroll.blocking_warnings()))),
           (SELECT count(*)::integer FROM payroll.members m WHERE m.payroll_id = p.id),
           (SELECT count(*)::integer FROM payroll.members m WHERE m.payroll_id = p.id AND m.excluded),
           coalesce((SELECT sum(e.gross) FROM payroll.entries e WHERE e.payroll_id = p.id), 0),
           s.contributions, s.health, s.pit, s.ppk,
           coalesce((SELECT sum(e.net) FROM payroll.entries e WHERE e.payroll_id = p.id), 0),
           s.deductions,
           coalesce((SELECT sum(e.payout) FROM payroll.entries e WHERE e.payroll_id = p.id), 0),
           s.employer, p.xmin::text::bigint
    FROM payroll.payrolls p
    LEFT JOIN payroll.payrolls c ON c.id = p.corrects_payroll_id
    CROSS JOIN sums s
    WHERE p.id = p_id;
END;
$$;

--changeset hr:payroll_app.payroll_create runOnChange:true splitStatements:false
--comment: Nowa lista płac (R1, R3, R4). Zwykła: rodzaj employment / mandate / specificWork, okres = miesiąc z p_period,
--comment: jedna na spółkę, rodzaj i miesiąc (period duplicate), skład z umów rodzaju (R3), numer RRRR/MM/UP|UZ|UD.
--comment: Korygująca: kind correction, lista korygowana tej spółki, zamknięta i nie korekta (correctsPayrollId
--comment: notFound / invalid), okres listy korygowanej, skład = umowy z jej wypłatami, numer <numer korygowanej>/K<n>.
--comment: Data wypłaty wymagana, nie przed początkiem okresu (beforeStart).
CREATE OR REPLACE PROCEDURE payroll_app.payroll_create(
    IN p_company_id uuid,
    IN p_kind text,
    IN p_period date,
    IN p_payment_date date,
    IN p_corrects_payroll_id uuid,
    INOUT p_id uuid
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_errors jsonb := '{}';
    v_corrected payroll.payrolls%ROWTYPE;
    v_period date := date_trunc('month', p_period)::date;
    v_number text;
    v_row payroll.payrolls%ROWTYPE;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    PERFORM FROM platform.companies c WHERE c.id = p_company_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'company not found' USING ERRCODE = 'U0002';
    END IF;

    IF p_kind IS NULL THEN
        v_errors := v_errors || '{"kind": ["required"]}';
    ELSIF p_kind NOT IN ('employment', 'mandate', 'specificWork', 'correction') THEN
        v_errors := v_errors || '{"kind": ["invalid"]}';
    ELSIF p_kind = 'correction' THEN
        SELECT * INTO v_corrected FROM payroll.payrolls p
        WHERE p.id = p_corrects_payroll_id AND p.company_id = p_company_id;
        IF p_corrects_payroll_id IS NULL THEN
            v_errors := v_errors || '{"correctsPayrollId": ["required"]}';
        ELSIF v_corrected.id IS NULL THEN
            v_errors := v_errors || '{"correctsPayrollId": ["notFound"]}';
        ELSIF v_corrected.status <> 'closed' OR v_corrected.kind = 'correction' THEN
            v_errors := v_errors || '{"correctsPayrollId": ["invalid"]}';
        ELSE
            v_period := v_corrected.period_from;
        END IF;
    ELSIF p_corrects_payroll_id IS NOT NULL THEN
        v_errors := v_errors || '{"correctsPayrollId": ["invalid"]}';
    END IF;
    IF p_kind IS DISTINCT FROM 'correction' THEN
        IF p_period IS NULL THEN
            v_errors := v_errors || '{"period": ["required"]}';
        ELSIF p_kind IN ('employment', 'mandate', 'specificWork') AND EXISTS (
            SELECT FROM payroll.payrolls p
            WHERE p.company_id = p_company_id AND p.kind = p_kind AND p.period_from = v_period
        ) THEN
            v_errors := v_errors || '{"period": ["duplicate"]}';
        END IF;
    END IF;
    IF p_payment_date IS NULL THEN
        v_errors := v_errors || '{"paymentDate": ["required"]}';
    ELSIF v_period IS NOT NULL AND p_payment_date < v_period THEN
        v_errors := v_errors || '{"paymentDate": ["beforeStart"]}';
    END IF;
    IF v_errors <> '{}' THEN
        PERFORM platform.fail_validation(v_errors);
    END IF;

    IF p_kind = 'correction' THEN
        v_number := v_corrected.number || '/K' || (
            SELECT count(*) + 1 FROM payroll.payrolls p WHERE p.corrects_payroll_id = v_corrected.id);
    ELSE
        v_number := to_char(v_period, 'YYYY/MM') || '/'
            || CASE p_kind WHEN 'employment' THEN 'UP' WHEN 'mandate' THEN 'UZ' ELSE 'UD' END;
    END IF;
    INSERT INTO payroll.payrolls (company_id, number, kind, period_from, period_to, payment_date, corrects_payroll_id,
                                  created_by)
    VALUES (p_company_id, v_number, p_kind, v_period, (v_period + interval '1 month - 1 day')::date, p_payment_date,
            v_corrected.id, platform.actor_id())
    RETURNING * INTO v_row;
    p_id := v_row.id;

    IF p_kind = 'correction' THEN
        INSERT INTO payroll.members (payroll_id, employment_id)
        SELECT p_id, e.employment_id FROM payroll.entries e WHERE e.payroll_id = v_corrected.id;
    ELSE
        PERFORM payroll.members_sync(v_row);
    END IF;
    PERFORM platform.audit('payroll', p_id::text, 'create', NULL, to_jsonb(v_row));
END;
$$;

--changeset hr:payroll_app.payroll_member_list runOnChange:true splitStatements:false
--comment: Skład listy (R3): umowy z osobą, wyłączenie i wypłata (gdy naliczona). Kadry, Administrator.
DROP FUNCTION IF EXISTS payroll_app.payroll_member_list(uuid);
CREATE FUNCTION payroll_app.payroll_member_list(p_payroll_id uuid)
    RETURNS TABLE (
        employment_id uuid,
        employment_number text,
        employee_id uuid,
        employee_number text,
        acronym text,
        first_name text,
        last_name text,
        excluded boolean,
        entry_id uuid
    )
    LANGUAGE plpgsql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    IF NOT EXISTS (SELECT FROM payroll.payrolls p WHERE p.id = p_payroll_id) THEN
        RAISE EXCEPTION 'payroll not found' USING ERRCODE = 'U0002';
    END IF;
    RETURN QUERY
    SELECT m.employment_id, x.number, e.id, e.number, e.acronym, e.first_name, e.last_name, m.excluded, n.id
    FROM payroll.members m
    JOIN employment.employments x ON x.id = m.employment_id
    JOIN employees.employees e ON e.id = x.employee_id
    LEFT JOIN payroll.entries n ON n.payroll_id = m.payroll_id AND n.employment_id = m.employment_id
    WHERE m.payroll_id = p_payroll_id
    ORDER BY e.last_name, e.first_name, x.number;
END;
$$;

--changeset hr:payroll_app.payroll_members_set runOnChange:true splitStatements:false
--comment: Wyłączenie umów z listy albo przywrócenie (R3) przed zamknięciem; umowy muszą być w składzie
--comment: (employmentIds notFound). Naliczona lista staje się nieaktualna (R2).
CREATE OR REPLACE PROCEDURE payroll_app.payroll_members_set(
    IN p_id uuid,
    IN p_row_version bigint,
    IN p_employment_ids uuid[],
    IN p_excluded boolean
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_row payroll.payrolls%ROWTYPE;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    v_row := payroll.payroll_lock(p_id, p_row_version);
    IF v_row.status = 'closed' THEN
        RAISE EXCEPTION 'payroll is closed' USING ERRCODE = 'U0003', HINT = 'closed';
    END IF;
    IF p_excluded IS NULL THEN
        PERFORM platform.fail_validation('{"excluded": ["required"]}');
    END IF;
    IF coalesce(cardinality(p_employment_ids), 0) = 0 OR EXISTS (
        SELECT FROM unnest(p_employment_ids) x(id)
        WHERE NOT EXISTS (SELECT FROM payroll.members m WHERE m.payroll_id = p_id AND m.employment_id = x.id)
    ) THEN
        PERFORM platform.fail_validation('{"employmentIds": ["notFound"]}');
    END IF;
    UPDATE payroll.members m SET excluded = p_excluded
    WHERE m.payroll_id = p_id AND m.employment_id = ANY (p_employment_ids);
    UPDATE payroll.payrolls p SET is_stale = true WHERE p.id = p_id AND p.status = 'calculated';
    PERFORM platform.audit('payroll', p_id::text, 'update', NULL,
        jsonb_build_object('operation', 'members', 'employmentIds', p_employment_ids, 'excluded', p_excluded));
END;
$$;

--changeset hr:payroll_app.payroll_calculate runOnChange:true splitStatements:false
--comment: Naliczenie albo przeliczenie listy (R2–R24); zamknięta — U0003 closed.
CREATE OR REPLACE PROCEDURE payroll_app.payroll_calculate(IN p_id uuid, IN p_row_version bigint)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_row payroll.payrolls%ROWTYPE;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    v_row := payroll.payroll_lock(p_id, p_row_version);
    IF v_row.status = 'closed' THEN
        RAISE EXCEPTION 'payroll is closed' USING ERRCODE = 'U0003', HINT = 'closed';
    END IF;
    PERFORM payroll.calculate(p_id);
    PERFORM platform.audit('payroll', p_id::text, 'update', jsonb_build_object('status', v_row.status),
        (SELECT jsonb_build_object('operation', 'calculate', 'status', p.status,
                                   'entries', (SELECT count(*) FROM payroll.entries e WHERE e.payroll_id = p.id))
         FROM payroll.payrolls p WHERE p.id = p_id));
END;
$$;

--changeset hr:payroll_app.payroll_close runOnChange:true splitStatements:false
--comment: Zamknięcie listy (R25): tylko naliczona (notCalculated), aktualna (stale) i bez wypłat z ostrzeżeniem
--comment: blokującym (unsupportedCases — przypadki, których system nie liczy, payroll.blocking_warnings). Zajęcia
--comment: z ściągniętą całą kwotą przechodzą w paidOff z datą wypłaty (006 R19).
CREATE OR REPLACE PROCEDURE payroll_app.payroll_close(IN p_id uuid, IN p_row_version bigint)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_row payroll.payrolls%ROWTYPE;
    v_after payroll.payrolls%ROWTYPE;
    v_garnishment record;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    v_row := payroll.payroll_lock(p_id, p_row_version);
    IF v_row.status <> 'calculated' THEN
        RAISE EXCEPTION 'payroll is not calculated' USING ERRCODE = 'U0003', HINT = 'notCalculated';
    END IF;
    IF v_row.is_stale THEN
        RAISE EXCEPTION 'payroll is stale' USING ERRCODE = 'U0003', HINT = 'stale';
    END IF;
    IF EXISTS (
        SELECT FROM payroll.entries e
        CROSS JOIN LATERAL jsonb_array_elements(e.warnings) w
        WHERE e.payroll_id = p_id AND w ->> 'code' = ANY (payroll.blocking_warnings())
    ) THEN
        RAISE EXCEPTION 'payroll has cases the system does not calculate' USING ERRCODE = 'U0003',
            HINT = 'unsupportedCases';
    END IF;
    UPDATE payroll.payrolls p SET status = 'closed', closed_at = now(), closed_by = platform.actor_id()
    WHERE p.id = p_id
    RETURNING * INTO v_after;

    FOR v_garnishment IN
        SELECT g.* FROM pay_components.garnishments g
        WHERE g.status = 'active' AND g.total_amount IS NOT NULL
          AND g.id::text IN (
              SELECT l.trace ->> 'garnishmentId' FROM payroll.lines l
              JOIN payroll.entries e ON e.id = l.entry_id
              WHERE e.payroll_id = p_id AND l.code IN ('ALIM', 'KOMOR')
          )
          AND payroll.garnishment_collected(g.id) >= g.total_amount
        FOR UPDATE
    LOOP
        UPDATE pay_components.garnishments g
        SET status = 'paidOff', status_date = greatest(v_row.payment_date, g.received_at)
        WHERE g.id = v_garnishment.id;
        PERFORM platform.audit('garnishment', v_garnishment.id::text, 'update', to_jsonb(v_garnishment),
            (SELECT to_jsonb(g) FROM pay_components.garnishments g WHERE g.id = v_garnishment.id));
    END LOOP;
    PERFORM platform.audit('payroll', p_id::text, 'update', to_jsonb(v_row),
        to_jsonb(v_after) || '{"operation": "close"}');
END;
$$;

--changeset hr:payroll_app.payroll_delete runOnChange:true splitStatements:false
--comment: Usunięcie listy otwartej albo naliczonej (R2); zamknięta — U0003 closed.
CREATE OR REPLACE PROCEDURE payroll_app.payroll_delete(IN p_id uuid, IN p_row_version bigint)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_row payroll.payrolls%ROWTYPE;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    v_row := payroll.payroll_lock(p_id, p_row_version);
    IF v_row.status = 'closed' THEN
        RAISE EXCEPTION 'payroll is closed' USING ERRCODE = 'U0003', HINT = 'closed';
    END IF;
    DELETE FROM payroll.payrolls p WHERE p.id = p_id;
    PERFORM platform.audit('payroll', p_id::text, 'delete', to_jsonb(v_row), NULL);
END;
$$;

--changeset hr:payroll_app.payroll_ppk_paid runOnChange:true splitStatements:false
--comment: Data przekazania wpłat PPK zamkniętej listy do instytucji finansowej (R19): od niej wpłaty pracodawcy są
--comment: przychodem pracownika — liczy go lista z wypłatą w tym miesiącu nie wcześniej niż w tym dniu. Tylko lista
--comment: zamknięta (payrollNotClosed), data nie przed datą wypłaty listy (paidOn beforePayment) i nie z przyszłości (future).
--comment: Zmiana albo usunięcie daty, gdy wpłaty są już przychodem na zamkniętej liście — U0003 ppkIncomeTaxed. Naliczone
--comment: listy z tymi wpłatami albo z wypłatą w miesiącu nowej daty stają się nieaktualne.
CREATE OR REPLACE PROCEDURE payroll_app.payroll_ppk_paid(IN p_id uuid, IN p_row_version bigint, IN p_paid_on date)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_row payroll.payrolls%ROWTYPE;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    v_row := payroll.payroll_lock(p_id, p_row_version);
    IF v_row.status <> 'closed' THEN
        RAISE EXCEPTION 'payroll is not closed' USING ERRCODE = 'U0003', HINT = 'payrollNotClosed';
    END IF;
    IF p_paid_on IS NOT NULL AND p_paid_on < v_row.payment_date THEN
        PERFORM platform.fail_validation('{"paidOn": ["beforePayment"]}');
    END IF;
    IF p_paid_on > current_date THEN
        PERFORM platform.fail_validation('{"paidOn": ["future"]}');
    END IF;
    IF p_paid_on IS NOT DISTINCT FROM v_row.ppk_paid_on THEN
        RETURN;
    END IF;
    IF EXISTS (
        SELECT FROM payroll.ppk_taxed_entries(ARRAY['closed']) t
        JOIN payroll.entries e ON e.id = t.entry_id AND e.payroll_id = p_id
    ) THEN
        RAISE EXCEPTION 'ppk contributions already taxed' USING ERRCODE = 'U0003', HINT = 'ppkIncomeTaxed';
    END IF;

    UPDATE payroll.payrolls p SET is_stale = true
    WHERE p.status = 'calculated' AND p.company_id = v_row.company_id
      AND (p.id IN (SELECT t.taxed_on_payroll_id FROM payroll.ppk_taxed_entries(ARRAY['calculated']) t
                    JOIN payroll.entries e ON e.id = t.entry_id AND e.payroll_id = p_id)
           OR (p_paid_on IS NOT NULL AND date_trunc('month', p.payment_date) = date_trunc('month', p_paid_on)
               AND p.payment_date >= p_paid_on));
    UPDATE payroll.payrolls p SET ppk_paid_on = p_paid_on WHERE p.id = p_id;
    PERFORM platform.audit('payroll', p_id::text, 'update', jsonb_build_object('ppk_paid_on', v_row.ppk_paid_on),
        jsonb_build_object('ppk_paid_on', p_paid_on, 'operation', 'ppkPaid'));
END;
$$;
