--liquibase formatted sql

--changeset hr:documents_app.optima_export runOnChange:true splitStatements:false
--comment: Kartoteki spółki do importu w Comarch ERP Optima (011 R1–R5): wskazane p_ids albo — gdy puste — pasujące do
--comment: filtra listy (employees.matching_ids). Tylko dane o potwierdzonym znaczeniu w arkuszu Optimy: akronim,
--comment: imiona, nazwisko, data urodzenia, PESEL, archiwalny (były pracownik), bieżące adresy zamieszkania
--comment: i korespondencyjny (ulica rozdzielona jak w PIT-11), e-mail. Kadry i Administrator; odczyt PESEL każdej
--comment: kartoteki w audycie odczytów (`optima_export`). Spółka wymagana — import w Optimie jest do bazy jednej firmy.
DROP FUNCTION IF EXISTS documents_app.optima_export(uuid, uuid[], text, text);
CREATE FUNCTION documents_app.optima_export(
    p_company_id uuid,
    p_ids uuid[],
    p_search text DEFAULT NULL,
    p_status text DEFAULT NULL
)
    RETURNS TABLE (
        employee_id uuid,
        acronym text,
        first_name text,
        second_name text,
        last_name text,
        birth_date date,
        pesel text,
        archived boolean,
        email text,
        residence_country text,
        residence_city text,
        residence_street text,
        residence_house text,
        residence_apartment text,
        residence_postal_code text,
        correspondence_country text,
        correspondence_city text,
        correspondence_street text,
        correspondence_house text,
        correspondence_apartment text,
        correspondence_postal_code text
    )
    LANGUAGE plpgsql
    VOLATILE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_ids uuid[];
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    IF p_company_id IS NULL THEN
        PERFORM platform.fail_validation('{"companyId": ["required"]}');
    ELSIF NOT EXISTS (SELECT FROM platform.companies c WHERE c.id = p_company_id) THEN
        PERFORM platform.fail_validation('{"companyId": ["notFound"]}');
    END IF;

    v_ids := CASE WHEN coalesce(cardinality(p_ids), 0) > 0 THEN p_ids
                  ELSE ARRAY(SELECT employees.matching_ids(p_company_id, p_search, p_status)) END;

    PERFORM platform.read_audit(e.id, 'optima_export', e.id::text)
    FROM employees.employees e
    WHERE e.id = ANY (v_ids) AND e.company_id = p_company_id;

    RETURN QUERY
    SELECT e.id, e.acronym, e.first_name, e.second_name, e.last_name, e.birth_date,
           employees.decrypt(e.pesel_enc),
           employment.employee_status(e.id, current_date) = 'former',
           e.email,
           r.country, r.city, rp.street, rp.house, rp.apartment, r.postal_code,
           c.country, c.city, cp.street, cp.house, cp.apartment, c.postal_code
    FROM employees.employees e
    LEFT JOIN LATERAL (
        SELECT a.* FROM employees.addresses a
        WHERE a.employee_id = e.id AND a.kind = 'residence' AND a.valid_from <= current_date
          AND (a.valid_to IS NULL OR a.valid_to >= current_date)
    ) r ON true
    LEFT JOIN LATERAL documents.address_parts(r.street) rp ON r.id IS NOT NULL
    LEFT JOIN LATERAL (
        SELECT a.* FROM employees.addresses a
        WHERE a.employee_id = e.id AND a.kind = 'correspondence' AND a.valid_from <= current_date
          AND (a.valid_to IS NULL OR a.valid_to >= current_date)
    ) c ON true
    LEFT JOIN LATERAL documents.address_parts(c.street) cp ON c.id IS NOT NULL
    WHERE e.id = ANY (v_ids) AND e.company_id = p_company_id
    ORDER BY e.last_name, e.first_name, e.acronym;
END;
$$;
