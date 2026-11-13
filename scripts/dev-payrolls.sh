#!/usr/bin/env bash
# Tylko środowisko deweloperskie: zakłada przykładowe listy płac spółki „Demo” przez API (logowanie deweloperskie
# jako Kadry) — wrzesień i październik 2026 zamknięte, listopad naliczony — oraz PIT-11 za 2026. Listę, która już jest
# (409 duplicate), pomija. Potrzebne do zrzutów ekranu (apps/web: yarn e2e:screenshots).
set -euo pipefail
api=${HR_API_URL:-http://127.0.0.1:6300/api}
company=${HR_DEMO_COMPANY_ID:-01a1d0db-7fa0-76e5-972d-100e3bd96785}

token=$(curl -sf -H 'content-type: application/json' -d '{"role":"kadry"}' "$api/auth/dev-login" | jq -r .token)
auth=(-H "Authorization: Bearer $token" -H 'content-type: application/json')

payroll() { # $1 rodzaj, $2 miesiąc RRRR-MM, $3 data wypłaty, $4 close|keep
    local id version
    id=$(curl -s "${auth[@]}" -d "{\"kind\":\"$1\",\"period\":\"$2-01\",\"paymentDate\":\"$3\"}" \
        "$api/companies/$company/payrolls" | jq -r '.id // empty')
    if [[ -z $id ]]; then echo "pominięta: $1 $2 (już jest)"; return; fi
    curl -sf "${auth[@]}" -d '{}' "$api/payrolls/$id/calculate" >/dev/null
    if [[ $4 == close ]]; then
        version=$(curl -sf "${auth[@]}" "$api/payrolls/$id" | jq -r .rowVersion)
        curl -sf "${auth[@]}" -d "{\"rowVersion\":$version}" "$api/payrolls/$id/close" >/dev/null
    fi
    echo "lista $1 $2: $4"
}

payroll employment 2026-09 2026-09-30 close
payroll employment 2026-10 2026-10-31 close
payroll mandate 2026-10 2026-10-31 close
payroll employment 2026-11 2026-11-30 keep
curl -sf "${auth[@]}" -d '{}' "$api/companies/$company/pit11/2026" >/dev/null && echo "PIT-11 2026: wygenerowane"
