--liquibase formatted sql

--changeset hr:documents.require_employee_access runOnChange:true splitStatements:false
--comment: Kadry i Administrator — każda kartoteka; Pracownik — tylko powiązana z kontem (R13). Zwraca true, gdy
--comment: czyta Kadry albo Administrator (Pracownik widzi tylko dokumenty ostateczne).
CREATE OR REPLACE FUNCTION documents.require_employee_access(p_employee_id uuid)
    RETURNS boolean
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    IF EXISTS (
        SELECT FROM platform.user_roles r JOIN platform.users u ON u.id = r.user_id
        WHERE r.user_id = platform.actor_id() AND u.is_active AND r.role IN ('kadry', 'administrator')
    ) THEN
        IF NOT EXISTS (SELECT FROM employees.employees e WHERE e.id = p_employee_id) THEN
            RAISE EXCEPTION 'employee not found' USING ERRCODE = 'U0002';
        END IF;
        RETURN true;
    END IF;
    PERFORM platform.require_role('pracownik');
    IF NOT EXISTS (
        SELECT FROM platform.user_employee_links l
        WHERE l.user_id = platform.actor_id() AND l.employee_id = p_employee_id
    ) THEN
        RAISE EXCEPTION 'forbidden' USING ERRCODE = 'U0004';
    END IF;
    RETURN false;
END;
$$;

--changeset hr:documents.address_parts runOnChange:true splitStatements:false
--comment: Ulica, numer domu i lokalu z jednego pola adresu kartoteki (np. „ul. Lipowa 1/2” → Lipowa, 1, 2) do pól
--comment: PIT-11 (poz. 23–25). Bez numeru na końcu — całość jako ulica.
CREATE OR REPLACE FUNCTION documents.address_parts(
    p_street text,
    OUT street text,
    OUT house text,
    OUT apartment text
)
    LANGUAGE plpgsql
    IMMUTABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_text text := regexp_replace(btrim(p_street), '^(ul\.|ulica)\s*', '', 'i');
    v_match text[];
BEGIN
    v_match := regexp_match(v_text, '^(.*\S)\s+(\d+[A-Za-z]?)(\s*/\s*(\S+))?$');
    IF v_match IS NULL THEN
        street := v_text;
    ELSE
        street := v_match[1];
        house := v_match[2];
        apartment := v_match[4];
    END IF;
END;
$$;

--changeset hr:documents.pit11_entries runOnChange:true splitStatements:false
--comment: Wypłaty kartoteki z zamkniętych list spółki z datą wypłaty w roku (R5) z kwotami do PIT-11: przychód
--comment: opodatkowany z wpłatą pracodawcy PPK (R6 [?]), przychód zwolniony (ZWOL-MLODZI), koszty (faktycznie
--comment: uwzględnione: KUP, KUP50), zaliczka (PIT), składki społeczne pracownika i część odliczona od dochodu (ślad
--comment: PODST-PIT), zdrowotna. Wiersz części E: umowa o pracę 1 (do 26 lat w dniu wypłaty — 2), zlecenie 6 (7),
--comment: dzieło 5. Wypłaty z ryczałtem (PIT-RYCZ) pomijane — idą do PIT-8AR. youth — zwolnienie art. 21 ust. 1 pkt 148
--comment: (do 26 lat bez rezygnacji), inaczej pkt 152–154 z oświadczeń (return/family/senior).
CREATE OR REPLACE FUNCTION documents.pit11_entries(p_employee_id uuid, p_year integer)
    RETURNS TABLE (
        entry_id uuid,
        payroll_id uuid,
        employment_id uuid,
        contract_kind text,
        payment_date date,
        row_no integer,
        young boolean,
        youth boolean,
        income numeric,
        exempt numeric,
        costs numeric,
        pit numeric,
        social numeric,
        social_deductible numeric,
        health numeric,
        increased_costs boolean,
        reliefs text[]
    )
    LANGUAGE sql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    WITH ent AS (
        SELECT n.id, p.id AS payroll_id, n.employment_id, m.kind AS contract_kind, p.payment_date, n.terms_id,
               payroll.age_on(e.birth_date, p.payment_date) < 26 AS young,
               coalesce(sum(l.amount) FILTER (WHERE (l.side = 'income' AND l.taxable)
                                              OR l.code IN ('PPK-PR', 'PPK-PRD')), 0) AS taxable,
               coalesce(sum(l.amount) FILTER (WHERE l.code = 'ZWOL-MLODZI'), 0) AS exempt,
               coalesce(sum(l.amount) FILTER (WHERE l.code IN ('KUP', 'KUP50')), 0) AS costs,
               coalesce(sum(l.amount) FILTER (WHERE l.code = 'PIT'), 0) AS pit,
               coalesce(sum(l.amount) FILTER (WHERE l.code IN ('ZUS-E-P', 'ZUS-R-P', 'ZUS-C-P')), 0) AS social,
               sum((l.trace ->> 'socialDeductible')::numeric) FILTER (WHERE l.code = 'PODST-PIT') AS social_ded,
               coalesce(sum(l.amount) FILTER (WHERE l.code = 'ZDROW'), 0) AS health,
               bool_or(l.code = 'PIT-RYCZ') AS flat
        FROM payroll.entries n
        JOIN employees.employees e ON e.id = n.employee_id
        JOIN payroll.payrolls p ON p.id = n.payroll_id AND p.status = 'closed' AND p.company_id = e.company_id
        JOIN employment.employments m ON m.id = n.employment_id
        JOIN payroll.lines l ON l.entry_id = n.id
        WHERE n.employee_id = p_employee_id
          AND p.payment_date >= make_date(p_year, 1, 1) AND p.payment_date < make_date(p_year + 1, 1, 1)
        GROUP BY n.id, p.id, m.kind, e.birth_date
    )
    SELECT x.id, x.payroll_id, x.employment_id, x.contract_kind, x.payment_date,
           CASE x.contract_kind
               WHEN 'employment' THEN CASE WHEN x.young THEN 2 ELSE 1 END
               WHEN 'mandate' THEN CASE WHEN x.young THEN 7 ELSE 6 END
               ELSE 5
           END,
           x.young,
           x.young AND NOT EXISTS (
               SELECT FROM tax_declarations.in_force(p_employee_id, x.payment_date) d
               WHERE d.kind = 'youthReliefWaiver'
           ),
           x.taxable - x.exempt, x.exempt, x.costs, x.pit, x.social,
           coalesce(x.social_ded, CASE WHEN x.exempt = 0 THEN x.social ELSE 0 END),
           x.health,
           x.contract_kind = 'employment' AND (
               (SELECT t.cost_kind FROM employment.terms t WHERE t.id = x.terms_id) = 'increased'
               OR EXISTS (SELECT FROM tax_declarations.in_force(p_employee_id, x.payment_date) d
                          WHERE d.kind = 'increasedCosts')),
           coalesce((SELECT array_agg(DISTINCT d.kind) FROM tax_declarations.in_force(p_employee_id, x.payment_date) d
                     WHERE d.kind IN ('returnRelief', 'familyRelief', 'seniorRelief')), '{}')
    FROM ent x
    WHERE NOT x.flat;
$$;

--changeset hr:documents.pit11_compute runOnChange:true splitStatements:false
--comment: Treść PIT-11(29) kartoteki za rok (R5–R6): pozycje pod nazwami elementów XSD 13535 (P_11, P_28–P_65,
--comment: P_95–P_97, P_109–P_122), płatnik (spółka), podatnik bez PESEL (imię, nazwisko, data urodzenia, adres
--comment: zamieszkania aktualny dziś, a bez niego ostatni), urząd skarbowy podatnika (poz. 6), listy źródłowe
--comment: i brakujące dane (missing: pesel, address, taxOffice). Wiersz części E tylko z kwotami; w części E przychód bez
--comment: przychodu zwolnionego (objaśnienie 13). Składki: poz. 95 — odliczone od dochodu w wierszach 1, 5, 6;
--comment: poz. 96 — w wierszach 2 i 7; poz. 97 — od przychodu zwolnionego. Poz. 28 (tylko przy wierszu 1) — z jednego (1)
--comment: albo więcej (2) stosunków pracy, podwyższone (3, 4). Poz. 121 — 2 (bez PIT-R). Kwoty ujemne (korekty) — 0.
CREATE OR REPLACE FUNCTION documents.pit11_compute(p_employee_id uuid, p_year integer)
    RETURNS jsonb
    LANGUAGE plpgsql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_employee record;
    v_company record;
    v_address record;
    v_parts record;
    v_office record;
    v_s record;
    v_row record;
    v_pos jsonb := jsonb_build_object('P_11', 1);
    v_missing text[] := '{}';
    v_income numeric;
    v_costs numeric;
    v_columns text[];
BEGIN
    SELECT e.*, e.pesel_enc IS NOT NULL AS has_pesel INTO v_employee
    FROM employees.employees e WHERE e.id = p_employee_id;
    SELECT c.nip, c.name INTO v_company FROM platform.companies c WHERE c.id = v_employee.company_id;
    SELECT a.street, a.postal_code, a.city, a.country INTO v_address
    FROM employees.addresses a
    WHERE a.employee_id = p_employee_id AND a.kind = 'residence'
    ORDER BY (current_date BETWEEN a.valid_from AND coalesce(a.valid_to, 'infinity')) DESC, a.valid_from DESC
    LIMIT 1;
    SELECT o.code, o.name INTO v_office FROM dictionaries.tax_offices o WHERE o.id = v_employee.tax_office_id;

    IF NOT v_employee.has_pesel THEN
        v_missing := array_append(v_missing, 'pesel');
    END IF;
    IF v_address.street IS NULL THEN
        v_missing := array_append(v_missing, 'address');
    ELSE
        SELECT * INTO v_parts FROM documents.address_parts(v_address.street);
    END IF;
    IF v_office.code IS NULL THEN
        v_missing := array_append(v_missing, 'taxOffice');
    END IF;

    -- Część E: wiersz → kolumny (przychód, koszty, dochód, zaliczka); dzieło i zlecenie bez kolumny e.
    FOR v_row IN
        SELECT x.row_no, sum(x.income) AS income, sum(x.costs) AS costs, sum(x.pit) AS pit
        FROM documents.pit11_entries(p_employee_id, p_year) x
        GROUP BY x.row_no
        ORDER BY x.row_no
    LOOP
        IF v_row.income = 0 AND v_row.costs = 0 AND v_row.pit = 0 THEN
            CONTINUE;
        END IF;
        v_columns := CASE v_row.row_no
            WHEN 1 THEN ARRAY['P_29', 'P_30', 'P_31', 'P_33']
            WHEN 2 THEN ARRAY['P_36', 'P_37', 'P_38', 'P_40']
            WHEN 5 THEN ARRAY['P_54', 'P_55', 'P_56', 'P_57']
            WHEN 6 THEN ARRAY['P_58', 'P_59', 'P_60', 'P_61']
            ELSE ARRAY['P_62', 'P_63', 'P_64', 'P_65']
        END;
        v_income := greatest(round(v_row.income, 2), 0);
        v_costs := greatest(round(v_row.costs, 2), 0);
        v_pos := v_pos || jsonb_build_object(v_columns[1], v_income, v_columns[3], greatest(v_income - v_costs, 0),
                                             v_columns[4], greatest(round(v_row.pit), 0));
        IF v_costs > 0 THEN
            v_pos := v_pos || jsonb_build_object(v_columns[2], v_costs);
        END IF;
    END LOOP;

    SELECT count(*) AS entry_count,
           coalesce(jsonb_agg(DISTINCT x.payroll_id), '[]') AS payroll_ids,
           count(DISTINCT x.employment_id) FILTER (WHERE x.row_no IN (1, 2)) AS employments,
           coalesce(bool_or(x.increased_costs) FILTER (WHERE x.row_no IN (1, 2)), false) AS increased,
           coalesce(sum(x.social_deductible) FILTER (WHERE x.row_no IN (1, 5, 6)), 0) AS p95,
           coalesce(sum(x.social_deductible) FILTER (WHERE x.row_no IN (2, 7)), 0) AS p96,
           coalesce(sum(x.social - x.social_deductible), 0) AS p97,
           coalesce(sum(x.exempt) FILTER (WHERE x.youth AND x.contract_kind = 'employment'), 0) AS p110,
           coalesce(sum(x.exempt) FILTER (WHERE x.youth AND x.contract_kind = 'mandate'), 0) AS p111,
           coalesce(sum(x.exempt) FILTER (WHERE NOT x.youth AND x.contract_kind = 'employment'), 0) AS p115,
           coalesce(sum(x.exempt) FILTER (WHERE NOT x.youth AND x.contract_kind = 'mandate'), 0) AS p116,
           (SELECT coalesce(array_agg(DISTINCT r.kind), '{}')
            FROM documents.pit11_entries(p_employee_id, p_year) y, unnest(y.reliefs) r(kind)
            WHERE NOT y.youth AND y.exempt > 0) AS reliefs,
           coalesce(sum(x.health), 0) AS p122
    INTO v_s
    FROM documents.pit11_entries(p_employee_id, p_year) x;

    -- Poz. 28 dotyczy kosztów z poz. 30 i w XSD należy do sekwencji wiersza 1.
    IF v_pos ? 'P_29' THEN
        v_pos := v_pos || jsonb_build_object('P_28',
            CASE WHEN v_s.employments > 1 THEN 2 ELSE 1 END + CASE WHEN v_s.increased THEN 2 ELSE 0 END);
    END IF;
    IF round(v_s.p95, 2) > 0 THEN v_pos := v_pos || jsonb_build_object('P_95', round(v_s.p95, 2)); END IF;
    IF round(v_s.p96, 2) > 0 THEN v_pos := v_pos || jsonb_build_object('P_96', round(v_s.p96, 2)); END IF;
    IF round(v_s.p97, 2) > 0 THEN v_pos := v_pos || jsonb_build_object('P_97', round(v_s.p97, 2)); END IF;
    IF v_s.p110 + v_s.p111 > 0 THEN
        v_pos := v_pos || jsonb_build_object('P_109', round(v_s.p110 + v_s.p111, 2));
        IF v_s.p110 > 0 THEN v_pos := v_pos || jsonb_build_object('P_110', round(v_s.p110, 2)); END IF;
        IF v_s.p111 > 0 THEN v_pos := v_pos || jsonb_build_object('P_111', round(v_s.p111, 2)); END IF;
    END IF;
    IF v_s.p115 + v_s.p116 > 0 THEN
        v_pos := v_pos || jsonb_build_object('P_114', round(v_s.p115 + v_s.p116, 2));
        IF v_s.p115 > 0 THEN v_pos := v_pos || jsonb_build_object('P_115', round(v_s.p115, 2)); END IF;
        IF v_s.p116 > 0 THEN v_pos := v_pos || jsonb_build_object('P_116', round(v_s.p116, 2)); END IF;
        IF 'returnRelief' = ANY (v_s.reliefs) THEN v_pos := v_pos || jsonb_build_object('P_118', 1); END IF;
        IF 'familyRelief' = ANY (v_s.reliefs) THEN v_pos := v_pos || jsonb_build_object('P_119', 1); END IF;
        IF 'seniorRelief' = ANY (v_s.reliefs) THEN v_pos := v_pos || jsonb_build_object('P_120', 1); END IF;
    END IF;
    v_pos := v_pos || jsonb_build_object('P_121', 2);
    IF round(v_s.p122, 2) > 0 THEN v_pos := v_pos || jsonb_build_object('P_122', round(v_s.p122, 2)); END IF;

    RETURN jsonb_build_object(
        'form', 'PIT-11(29)',
        'year', p_year,
        'payer', jsonb_build_object('nip', v_company.nip, 'name', v_company.name),
        'taxpayer', jsonb_build_object(
            'firstName', v_employee.first_name,
            'lastName', v_employee.last_name,
            'birthDate', v_employee.birth_date,
            'address', CASE WHEN v_address.street IS NOT NULL THEN jsonb_build_object(
                'country', v_address.country, 'street', v_parts.street, 'house', v_parts.house,
                'apartment', v_parts.apartment, 'city', v_address.city, 'postalCode', v_address.postal_code) END),
        'taxOffice', CASE WHEN v_office.code IS NOT NULL
                          THEN jsonb_build_object('code', v_office.code, 'name', v_office.name) END,
        'positions', v_pos,
        'payrollIds', v_s.payroll_ids,
        'entryCount', v_s.entry_count,
        'missing', to_jsonb(v_missing)
    );
END;
$$;

--changeset hr:documents.pit11_employees runOnChange:true splitStatements:false
--comment: Kartoteki spółki z wypłatą do PIT-11 w roku: zamknięta lista z datą wypłaty w roku, wypłata bez ryczałtu.
CREATE OR REPLACE FUNCTION documents.pit11_employees(p_company_id uuid, p_year integer)
    RETURNS SETOF uuid
    LANGUAGE sql
    STABLE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT DISTINCT n.employee_id
    FROM payroll.entries n
    JOIN payroll.payrolls p ON p.id = n.payroll_id AND p.status = 'closed'
    WHERE p.company_id = p_company_id
      AND p.payment_date >= make_date(p_year, 1, 1) AND p.payment_date < make_date(p_year + 1, 1, 1)
      AND NOT EXISTS (SELECT FROM payroll.lines l WHERE l.entry_id = n.id AND l.code = 'PIT-RYCZ');
$$;
