--liquibase formatted sql

--changeset hr:tax_declarations_app.declaration_list runOnChange:true splitStatements:false
--comment: Oświadczenia kartoteki ze statusem i wygasaniem (R10). Kadry i Administrator — każda kartoteka; Pracownik —
--comment: tylko kartoteka powiązana z jego kontem (R13). Bez kwot, więc bez audytu odczytu (R12).
CREATE OR REPLACE FUNCTION tax_declarations_app.declaration_list(p_employee_id uuid, p_only_active boolean)
    RETURNS TABLE (
        id uuid,
        kind text,
        share text,
        year_from integer,
        year_to integer,
        submitted_at date,
        valid_from date,
        valid_to date,
        withdrawn_at date,
        lapses_on date,
        status text,
        row_version bigint
    )
    LANGUAGE plpgsql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    IF NOT EXISTS (
        SELECT FROM platform.user_roles r JOIN platform.users u ON u.id = r.user_id
        WHERE r.user_id = platform.actor_id() AND u.is_active AND r.role IN ('kadry', 'administrator')
    ) THEN
        PERFORM platform.require_role('pracownik');
        IF NOT EXISTS (
            SELECT FROM platform.user_employee_links l
            WHERE l.user_id = platform.actor_id() AND l.employee_id = p_employee_id
        ) THEN
            RAISE EXCEPTION 'forbidden' USING ERRCODE = 'U0004';
        END IF;
    END IF;
    IF NOT EXISTS (SELECT FROM employees.employees e WHERE e.id = p_employee_id) THEN
        RAISE EXCEPTION 'employee not found' USING ERRCODE = 'U0002';
    END IF;

    RETURN QUERY
    SELECT x.id, x.kind, x.share, x.year_from, x.year_to, x.submitted_at, x.valid_from, x.valid_to, x.withdrawn_at,
           x.lapses_on, x.status, x.row_version
    FROM (
        SELECT d.*, l.lapses_on,
               tax_declarations.status(d.valid_from, d.valid_to, l.lapses_on, current_date) AS status,
               d.xmin::text::bigint AS row_version
        FROM tax_declarations.declarations d
        CROSS JOIN LATERAL (
            SELECT tax_declarations.lapses_on(d.employee_id, d.kind, d.valid_from) AS lapses_on
        ) l
        WHERE d.employee_id = p_employee_id
    ) x
    WHERE NOT coalesce(p_only_active, false) OR x.status IN ('active', 'future')
    ORDER BY x.valid_from DESC, x.kind;
END;
$$;

--changeset hr:tax_declarations_app.declaration_create runOnChange:true splitStatements:false
--comment: Nowy wpis (R2–R5). Bez końca: noAdvances i returnRelief dostają koniec naturalny (R4). Okres obejmuje
--comment: datę wypłaty zamkniętej listy płac — U0003 payrollClosed, naliczonej — lista nieaktualna (009 R2, R25).
CREATE OR REPLACE PROCEDURE tax_declarations_app.declaration_create(
    IN p_employee_id uuid,
    IN p_kind text,
    IN p_share text,
    IN p_year_from integer,
    IN p_year_to integer,
    IN p_submitted_at date,
    IN p_valid_from date,
    IN p_valid_to date,
    INOUT p_id uuid
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_kind text := nullif(btrim(p_kind), '');
    v_share text := nullif(btrim(p_share), '');
    v_errors jsonb;
    v_row tax_declarations.declarations%ROWTYPE;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    PERFORM FROM employees.employees e WHERE e.id = p_employee_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'employee not found' USING ERRCODE = 'U0002';
    END IF;

    v_errors := tax_declarations.validate(v_kind, v_share, p_year_from, p_year_to, p_submitted_at, p_valid_from,
        p_valid_to);
    IF v_errors <> '{}' THEN
        PERFORM platform.fail_validation(v_errors);
    END IF;

    PERFORM payroll.guard(p_employee_id, NULL, p_valid_from,
        coalesce(p_valid_to, tax_declarations.natural_end(v_kind, p_valid_from, p_year_to)), true);

    INSERT INTO tax_declarations.declarations (employee_id, kind, share, year_from, year_to, submitted_at, valid_from,
                                               valid_to, created_by)
    VALUES (p_employee_id, v_kind, v_share, p_year_from, p_year_to, p_submitted_at, p_valid_from,
            coalesce(p_valid_to, tax_declarations.natural_end(v_kind, p_valid_from, p_year_to)), platform.actor_id())
    RETURNING * INTO v_row;
    p_id := v_row.id;
    PERFORM platform.audit('tax_declaration', p_id::text, 'create', NULL, to_jsonb(v_row));
EXCEPTION
    WHEN exclusion_violation THEN
        PERFORM platform.fail_validation('{"validFrom": ["overlap"]}');
END;
$$;

--changeset hr:tax_declarations_app.declaration_end runOnChange:true splitStatements:false
--comment: Zakończenie wpisu wycofaniem złożonym w dniu, od miesiąca stop_from (R6): koniec = ostatni dzień miesiąca
--comment: poprzedniego. Obie daty puste cofają zakończenie (koniec wraca do naturalnego). Zmienione miesiące z datą
--comment: wypłaty zamkniętej listy płac — U0003 payrollClosed (009 R25).
CREATE OR REPLACE PROCEDURE tax_declarations_app.declaration_end(
    IN p_id uuid,
    IN p_row_version bigint,
    IN p_withdrawn_at date,
    IN p_stop_from date
)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_before tax_declarations.declarations%ROWTYPE;
    v_natural date;
    v_valid_to date;
    v_errors jsonb := '{}';
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    SELECT * INTO v_before FROM tax_declarations.declarations d WHERE d.id = p_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'tax declaration not found' USING ERRCODE = 'U0002';
    END IF;
    IF (SELECT d.xmin::text::bigint FROM tax_declarations.declarations d WHERE d.id = p_id) <> p_row_version THEN
        RAISE EXCEPTION 'tax declaration changed' USING ERRCODE = 'U0003';
    END IF;
    v_natural := tax_declarations.natural_end(v_before.kind, v_before.valid_from, v_before.year_to);

    IF p_withdrawn_at IS NULL AND p_stop_from IS NULL THEN
        v_valid_to := v_natural;
    ELSE
        IF p_withdrawn_at IS NULL THEN
            v_errors := v_errors || '{"withdrawnAt": ["required"]}';
        ELSIF p_withdrawn_at > current_date THEN
            v_errors := v_errors || '{"withdrawnAt": ["future"]}';
        ELSIF p_withdrawn_at < v_before.submitted_at THEN
            v_errors := v_errors || '{"withdrawnAt": ["beforeSubmission"]}';
        END IF;
        IF p_stop_from IS NULL THEN
            v_errors := v_errors || '{"stopFrom": ["required"]}';
        ELSIF extract(day FROM p_stop_from) <> 1 THEN
            v_errors := v_errors || '{"stopFrom": ["invalid"]}';
        ELSIF p_withdrawn_at IS NOT NULL AND NOT tax_declarations.month_allowed(p_withdrawn_at, p_stop_from) THEN
            v_errors := v_errors || '{"stopFrom": ["outsideAllowed"]}';
        ELSIF p_stop_from <= v_before.valid_from THEN
            v_errors := v_errors || '{"stopFrom": ["beforeFrom"]}';
        ELSIF p_stop_from - 1 > v_natural THEN
            v_errors := v_errors || '{"stopFrom": ["afterEnd"]}';
        END IF;
        IF v_errors <> '{}' THEN
            PERFORM platform.fail_validation(v_errors);
        END IF;
        v_valid_to := p_stop_from - 1;
    END IF;

    IF v_before.valid_to IS DISTINCT FROM v_valid_to THEN
        PERFORM payroll.guard(v_before.employee_id, NULL,
            least(coalesce(v_before.valid_to, 'infinity'), coalesce(v_valid_to, 'infinity')) + 1,
            nullif(greatest(coalesce(v_before.valid_to, 'infinity'), coalesce(v_valid_to, 'infinity')), 'infinity'),
            true);
    END IF;
    UPDATE tax_declarations.declarations d
    SET valid_to = v_valid_to, withdrawn_at = CASE WHEN p_stop_from IS NULL THEN NULL ELSE p_withdrawn_at END
    WHERE d.id = p_id;
    PERFORM platform.audit('tax_declaration', p_id::text, 'update',
        jsonb_build_object('valid_to', v_before.valid_to, 'withdrawn_at', v_before.withdrawn_at),
        jsonb_build_object('valid_to', v_valid_to, 'withdrawn_at', p_withdrawn_at));
EXCEPTION
    -- Cofnięcie zakończenia wydłuża wpis — może wejść na późniejszy wpis tego samego rodzaju.
    WHEN exclusion_violation THEN
        PERFORM platform.fail_validation('{"stopFrom": ["overlap"]}');
END;
$$;

--changeset hr:tax_declarations_app.declaration_delete runOnChange:true splitStatements:false
--comment: Usunięcie wpisu wprowadzonego przez pomyłkę (R9). Okres z datą wypłaty zamkniętej listy płac (wpis użyty
--comment: na liście) — U0003 payrollClosed, naliczonej — lista nieaktualna (009 R25).
CREATE OR REPLACE PROCEDURE tax_declarations_app.declaration_delete(IN p_id uuid, IN p_row_version bigint)
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_row tax_declarations.declarations%ROWTYPE;
BEGIN
    PERFORM platform.require_role('kadry', 'administrator');
    SELECT * INTO v_row FROM tax_declarations.declarations d WHERE d.id = p_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'tax declaration not found' USING ERRCODE = 'U0002';
    END IF;
    IF (SELECT d.xmin::text::bigint FROM tax_declarations.declarations d WHERE d.id = p_id) <> p_row_version THEN
        RAISE EXCEPTION 'tax declaration changed' USING ERRCODE = 'U0003';
    END IF;
    PERFORM payroll.guard(v_row.employee_id, NULL, v_row.valid_from, v_row.valid_to, true);

    DELETE FROM tax_declarations.declarations d WHERE d.id = p_id;
    PERFORM platform.audit('tax_declaration', p_id::text, 'delete', to_jsonb(v_row), NULL);
END;
$$;
