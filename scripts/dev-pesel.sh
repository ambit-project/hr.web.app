#!/usr/bin/env bash
# Tylko środowisko deweloperskie (docker compose z infrastructure/): nadaje fikcyjny, poprawny PESEL kartotekom, które
# go nie mają — z daty urodzenia, cyfry płci (imię kończące się na „a” = kobieta) i sumy kontrolnej. Szyfruje kluczem
# HR_DATA_KEY z infrastructure/.env, tak jak API (ADR-0009). Migracja tego nie zrobi, bo Liquibase nie zna klucza.
set -euo pipefail
cd "$(dirname "$0")/.."

env_file=infrastructure/.env
[[ -f $env_file ]] || { echo "Brak $env_file — najpierw infrastructure/start.sh" >&2; exit 1; }
key=$(grep -E '^HR_DATA_KEY=' "$env_file" | cut -d= -f2-)
[[ ${#key} -ge 32 ]] || { echo "HR_DATA_KEY w $env_file ma mniej niż 32 znaki" >&2; exit 1; }

docker compose -f infrastructure/docker-compose.yml exec -T postgres \
    psql -U postgres -d hr -v ON_ERROR_STOP=1 -v key="$key" <<'SQL'
SELECT set_config('hr.data_key', :'key', false);
DO $$
DECLARE
    r record;
    v_month integer;
    v_base text;
    v_sum integer;
    v_pesel text;
    v_serial integer;
    v_done integer := 0;
BEGIN
    FOR r IN
        SELECT e.id, e.first_name, e.birth_date FROM employees.employees e WHERE e.pesel_enc IS NULL ORDER BY e.id
    LOOP
        -- Miesiąc z przesunięciem stulecia: 1900–1999 bez zmian, 2000–2099 +20.
        v_month := extract(month FROM r.birth_date)::integer
            + CASE WHEN extract(year FROM r.birth_date) >= 2000 THEN 20 ELSE 0 END;
        v_serial := floor(random() * 1000)::integer;
        LOOP
            -- Cyfra płci: parzysta dla kobiet, nieparzysta dla mężczyzn.
            v_base := to_char(r.birth_date, 'YY') || lpad(v_month::text, 2, '0') || to_char(r.birth_date, 'DD')
                || lpad(v_serial::text, 3, '0')
                || CASE WHEN lower(r.first_name) LIKE '%a' THEN (2 * floor(random() * 5))::integer
                        ELSE (2 * floor(random() * 5) + 1)::integer END;
            SELECT sum(substr(v_base, i::int, 1)::int * w) INTO v_sum
            FROM unnest(ARRAY[1, 3, 7, 9, 1, 3, 7, 9, 1, 3]) WITH ORDINALITY AS t(w, i);
            v_pesel := v_base || ((10 - v_sum % 10) % 10)::text;
            EXIT WHEN NOT EXISTS (
                SELECT FROM employees.employees e WHERE e.pesel_hmac = employees.pesel_hash(v_pesel)
            );
            v_serial := (v_serial + 1) % 1000;
        END LOOP;
        IF NOT employees.pesel_valid(v_pesel) THEN
            RAISE EXCEPTION 'wygenerowany PESEL % jest niepoprawny', v_pesel;
        END IF;
        UPDATE employees.employees e
        SET pesel_enc = employees.encrypt(v_pesel), pesel_hmac = employees.pesel_hash(v_pesel)
        WHERE e.id = r.id;
        v_done := v_done + 1;
    END LOOP;
    RAISE NOTICE 'Nadano PESEL: % kartotekom', v_done;
END;
$$;
SQL
