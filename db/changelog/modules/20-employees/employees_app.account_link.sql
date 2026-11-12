--liquibase formatted sql

--changeset hr:employees_app.account_link runOnChange:true splitStatements:false
--comment: Powiązanie kartoteki z kontem z rolą Pracownik (R12), wyszukanym po loginie albo e-mailu. Zastępuje
--comment: poprzednie powiązanie tej kartoteki.
CREATE OR REPLACE PROCEDURE employees_app.account_link(IN p_employee_id uuid, IN p_account text)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_company_id uuid;
    v_user_id uuid;
    v_account text := lower(nullif(btrim(p_account), ''));
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    SELECT e.company_id INTO v_company_id FROM employees.employees e WHERE e.id = p_employee_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'employee not found' USING ERRCODE = 'U0002';
    END IF;

    IF v_account IS NULL THEN
        PERFORM platform.fail_validation('{"account": ["required"]}');
    END IF;
    SELECT u.id INTO v_user_id FROM platform.users u
    WHERE lower(u.login) = v_account OR lower(u.email) = v_account;
    IF v_user_id IS NULL THEN
        PERFORM platform.fail_validation('{"account": ["notFound"]}');
    END IF;
    IF NOT EXISTS (SELECT FROM platform.user_roles r WHERE r.user_id = v_user_id AND r.role = 'pracownik') THEN
        PERFORM platform.fail_validation('{"account": ["invalid"]}');
    END IF;
    IF EXISTS (
        SELECT FROM platform.user_employee_links l
        WHERE l.user_id = v_user_id AND l.company_id = v_company_id AND l.employee_id <> p_employee_id
    ) THEN
        PERFORM platform.fail_validation('{"account": ["duplicate"]}');
    END IF;

    DELETE FROM platform.user_employee_links l WHERE l.employee_id = p_employee_id;
    INSERT INTO platform.user_employee_links (user_id, employee_id, company_id, linked_by)
    VALUES (v_user_id, p_employee_id, v_company_id, platform.actor_id());
    PERFORM platform.audit('employee_link', p_employee_id::text, 'create', NULL,
        jsonb_build_object('userId', v_user_id));
END;
$$;

--changeset hr:employees_app.account_unlink runOnChange:true splitStatements:false
--comment: Usunięcie powiązania kartoteki z kontem (R12).
CREATE OR REPLACE PROCEDURE employees_app.account_unlink(IN p_employee_id uuid)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_user_id uuid;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    DELETE FROM platform.user_employee_links l WHERE l.employee_id = p_employee_id RETURNING l.user_id INTO v_user_id;
    IF v_user_id IS NOT NULL THEN
        PERFORM platform.audit('employee_link', p_employee_id::text, 'delete',
            jsonb_build_object('userId', v_user_id), NULL);
    END IF;
END;
$$;

--changeset hr:employees_app.read_audit_list runOnChange:true splitStatements:false
--comment: Odczyty kartoteki przez innych niż sam pracownik (R10, Administrator).
DROP FUNCTION IF EXISTS employees_app.read_audit_list(uuid, integer, integer);
CREATE FUNCTION employees_app.read_audit_list(
    p_employee_id uuid,
    p_limit integer DEFAULT 50,
    p_offset integer DEFAULT 0
)
    RETURNS TABLE (id bigint, at timestamptz, actor_name text, resource text, total_count bigint)
    LANGUAGE plpgsql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    PERFORM platform.require_role('administrator');
    RETURN QUERY
    SELECT r.id, r.at, coalesce(u.display_name, '?'), r.resource, count(*) OVER ()
    FROM platform.read_audit_entries r
    LEFT JOIN platform.users u ON u.id = r.actor_id
    WHERE r.employee_id = p_employee_id
    ORDER BY r.at DESC, r.id DESC
    LIMIT least(greatest(p_limit, 1), 100)
    OFFSET greatest(p_offset, 0);
END;
$$;
