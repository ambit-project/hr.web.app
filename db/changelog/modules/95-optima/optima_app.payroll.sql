--liquibase formatted sql

--changeset hr:optima_app.payroll_get runOnChange:true splitStatements:false
--comment: Listy płac pobrane z Optimy dla spółki za miesiąc (Kadry, Administrator), z wypłatami i elementami, jako jeden
--comment: dokument JSON. Kwoty pracownika — każdy rozpoznany pracownik dostaje wpis audytu odczytu (optima_payroll).
DROP FUNCTION IF EXISTS optima_app.payroll_get(uuid, integer, integer);
CREATE FUNCTION optima_app.payroll_get(p_company_id uuid, p_year integer, p_month integer)
    RETURNS jsonb
    LANGUAGE plpgsql
    VOLATILE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_database text;
    v_result jsonb;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    IF NOT EXISTS (SELECT FROM platform.companies c WHERE c.id = p_company_id) THEN
        RAISE EXCEPTION 'company not found' USING ERRCODE = 'U0002';
    END IF;
    SELECT l.database_name INTO v_database FROM optima.company_links l WHERE l.company_id = p_company_id;

    PERFORM platform.read_audit(w.employee_id, 'optima_payroll', l.id::text)
    FROM optima.payroll_lists l
    JOIN optima.payouts w ON w.payroll_list_id = l.id
    WHERE l.database_name = v_database AND l.year = p_year AND l.month = p_month AND w.employee_id IS NOT NULL;

    SELECT coalesce(jsonb_agg(jsonb_build_object(
               'id', l.id, 'optimaId', l.optima_id, 'number', l.number, 'name', l.name,
               'periodFrom', l.period_from, 'periodTo', l.period_to, 'documentDate', l.document_date,
               'isClosed', l.is_closed, 'importedAt', l.imported_at,
               'payouts', (
                   SELECT coalesce(jsonb_agg(jsonb_build_object(
                              'optimaId', w.optima_id, 'employeeId', w.employee_id, 'employeeCode', w.employee_code,
                              'employeeName', CASE WHEN e.id IS NOT NULL THEN e.last_name || ' ' || e.first_name END,
                              'gross', w.gross, 'net', w.net, 'isClosed', w.is_closed,
                              'elements', (
                                  SELECT coalesce(jsonb_agg(jsonb_build_object(
                                             'componentOptimaId', x.component_optima_id, 'name', x.name,
                                             'amount', x.amount) ORDER BY x.id), '[]')
                                  FROM optima.payout_elements x WHERE x.payout_id = w.id
                              )
                          ) ORDER BY w.employee_code, w.optima_id), '[]')
                   FROM optima.payouts w
                   LEFT JOIN employees.employees e ON e.id = w.employee_id
                   WHERE w.payroll_list_id = l.id
               )
           ) ORDER BY l.number, l.optima_id), '[]')
    INTO v_result
    FROM optima.payroll_lists l
    WHERE l.database_name = v_database AND l.year = p_year AND l.month = p_month;

    RETURN v_result;
END;
$$;
