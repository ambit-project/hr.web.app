--liquibase formatted sql

--changeset hr:optima.store_pay_types runOnChange:true splitStatements:false
--comment: Wynik ReadPayrollComponents: [{databaseName, taxNumber, components: [...]}]. Rodzaje płac bazy są nadpisywane;
--comment: rodzaj nieobecny w odczycie dostaje missing_since (powiązanie zostaje), obecny ponownie — traci je.
CREATE OR REPLACE FUNCTION optima.store_pay_types(p_companies jsonb)
    RETURNS void
    LANGUAGE plpgsql
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_company jsonb;
    v_database text;
BEGIN
    IF jsonb_typeof(p_companies) <> 'array' THEN
        PERFORM platform.fail_validation('{"payrollComponents": ["invalid"]}');
    END IF;

    FOR v_company IN SELECT x FROM jsonb_array_elements(p_companies) x LOOP
        v_database := nullif(btrim(v_company->>'databaseName'), '');
        IF v_database IS NULL OR jsonb_typeof(coalesce(v_company->'components', '[]')) <> 'array' THEN
            PERFORM platform.fail_validation('{"payrollComponents": ["invalid"]}');
        END IF;
        PERFORM optima.database_seen(v_database, v_company->>'taxNumber');

        INSERT INTO optima.pay_types AS p (
            database_name, optima_id, name, short_name, is_deduction, source_kind, standard_id, default_amount,
            is_pkzp, is_seizure, is_active, is_withdrawn, imported_at, missing_since
        )
        SELECT v_database, (c->>'optimaId')::integer, btrim(c->>'name'), nullif(btrim(c->>'shortName'), ''),
               (c->>'isDeduction')::boolean, (c->>'sourceKind')::integer, (c->>'standardId')::integer,
               (c->>'defaultAmount')::numeric(15, 2), coalesce((c->>'isPkzp')::boolean, false),
               coalesce((c->>'isSeizure')::boolean, false), coalesce((c->>'isActive')::boolean, true),
               coalesce((c->>'isWithdrawn')::boolean, false), now(), NULL
        FROM jsonb_array_elements(coalesce(v_company->'components', '[]')) c
        ON CONFLICT (database_name, optima_id) DO UPDATE
            SET name = excluded.name, short_name = excluded.short_name, is_deduction = excluded.is_deduction,
                source_kind = excluded.source_kind, standard_id = excluded.standard_id,
                default_amount = excluded.default_amount, is_pkzp = excluded.is_pkzp,
                is_seizure = excluded.is_seizure, is_active = excluded.is_active,
                is_withdrawn = excluded.is_withdrawn, imported_at = excluded.imported_at, missing_since = NULL,
                -- Rodzaj, który w Optimie zmienił się z dodatku na potrącenie (albo odwrotnie), traci powiązanie.
                component_type_id = CASE WHEN p.is_deduction = excluded.is_deduction THEN p.component_type_id END;

        UPDATE optima.pay_types p SET missing_since = now()
        WHERE p.database_name = v_database AND p.missing_since IS NULL
          AND p.optima_id NOT IN (
              SELECT (c->>'optimaId')::integer FROM jsonb_array_elements(coalesce(v_company->'components', '[]')) c
          );
    END LOOP;
END;
$$;

--changeset hr:optima.store_payrolls runOnChange:true splitStatements:false
--comment: Wynik ReadPayrolls: listy płac bazy za miesiąc operacji zastępują poprzedni import (lista usunięta w Optimie
--comment: znika). Pracownik rozpoznany po id hr.web.app (PRA_ImportRowId), inaczej po akronimie w spółce, inaczej NULL.
CREATE OR REPLACE FUNCTION optima.store_payrolls(p_op optima.operations, p_payrolls jsonb)
    RETURNS void
    LANGUAGE plpgsql
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_list jsonb;
    v_payout jsonb;
    v_list_id uuid;
    v_payout_id uuid;
BEGIN
    IF jsonb_typeof(p_payrolls) <> 'object'
       OR p_payrolls->>'databaseName' IS DISTINCT FROM p_op.database_name
       OR (p_payrolls->>'year')::integer IS DISTINCT FROM p_op.year
       OR (p_payrolls->>'month')::integer IS DISTINCT FROM p_op.month
       OR jsonb_typeof(coalesce(p_payrolls->'lists', '[]')) <> 'array' THEN
        PERFORM platform.fail_validation('{"payrolls": ["mismatch"]}');
    END IF;
    PERFORM optima.database_seen(p_op.database_name, p_payrolls->>'taxNumber');

    DELETE FROM optima.payroll_lists l
    WHERE l.database_name = p_op.database_name AND l.year = p_op.year AND l.month = p_op.month
      AND l.optima_id NOT IN (
          SELECT (x->>'optimaId')::integer FROM jsonb_array_elements(coalesce(p_payrolls->'lists', '[]')) x
      );

    FOR v_list IN SELECT x FROM jsonb_array_elements(coalesce(p_payrolls->'lists', '[]')) x LOOP
        INSERT INTO optima.payroll_lists AS l (
            database_name, optima_id, year, month, number, name, period_from, period_to, document_date, is_closed,
            imported_at, operation_id
        )
        VALUES (
            p_op.database_name, (v_list->>'optimaId')::integer, p_op.year, p_op.month,
            coalesce(nullif(btrim(v_list->>'number'), ''), v_list->>'optimaId'), nullif(btrim(v_list->>'name'), ''),
            (v_list->>'periodFrom')::date, (v_list->>'periodTo')::date, (v_list->>'documentDate')::date,
            coalesce((v_list->>'isClosed')::boolean, false), now(), p_op.id
        )
        ON CONFLICT (database_name, optima_id) DO UPDATE
            SET year = excluded.year, month = excluded.month, number = excluded.number, name = excluded.name,
                period_from = excluded.period_from, period_to = excluded.period_to,
                document_date = excluded.document_date, is_closed = excluded.is_closed,
                imported_at = excluded.imported_at, operation_id = excluded.operation_id
        RETURNING l.id INTO v_list_id;

        DELETE FROM optima.payouts WHERE payroll_list_id = v_list_id;

        FOR v_payout IN SELECT x FROM jsonb_array_elements(coalesce(v_list->'payouts', '[]')) x LOOP
            INSERT INTO optima.payouts (
                payroll_list_id, optima_id, optima_employee_id, employee_id, employee_code, gross, net, is_closed
            )
            VALUES (
                v_list_id, (v_payout->>'optimaId')::integer, (v_payout->>'optimaEmployeeId')::integer,
                coalesce(
                    (SELECT e.id FROM employees.employees e
                     WHERE e.company_id = p_op.company_id AND e.id = optima.try_uuid(v_payout->>'employeeId')),
                    (SELECT e.id FROM employees.employees e
                     WHERE e.company_id = p_op.company_id
                       AND upper(e.acronym) = upper(btrim(v_payout->>'employeeCode')))
                ),
                nullif(btrim(v_payout->>'employeeCode'), ''), (v_payout->>'gross')::numeric(15, 2),
                (v_payout->>'net')::numeric(15, 2), coalesce((v_payout->>'isClosed')::boolean, false)
            )
            RETURNING id INTO v_payout_id;

            INSERT INTO optima.payout_elements (payout_id, optima_id, component_optima_id, name, amount)
            SELECT v_payout_id, (el->>'optimaId')::integer, (el->>'componentOptimaId')::integer,
                   coalesce(nullif(btrim(el->>'name'), ''), '?'), (el->>'amount')::numeric(15, 2)
            FROM jsonb_array_elements(coalesce(v_payout->'elements', '[]')) el;
        END LOOP;
    END LOOP;
END;
$$;
