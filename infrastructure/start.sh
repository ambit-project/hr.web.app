#!/usr/bin/env bash
# Stawia lokalny stos: PostgreSQL 18, role, migracje, API i front.
# Użycie: infrastructure/start.sh [--db-only]   (--db-only: tylko baza i migracje, API i front z dotnet run / yarn dev)
set -euo pipefail
cd "$(dirname "$0")"

if [[ ! -f .env ]]; then
    cp .env-sample .env
    echo "Utworzono infrastructure/.env z .env-sample (wartości deweloperskie)."
fi

if [[ "${1:-}" == "--db-only" ]]; then
    docker compose up -d --wait postgres
    docker compose up db-init migrate --exit-code-from migrate
else
    docker compose up -d --build --wait
fi

set -a; source .env; set +a
echo "API:  http://127.0.0.1:${HR_API_PORT:-6300} (OpenAPI: /openapi/v1.json, Scalar: /scalar)"
echo "Web:  http://127.0.0.1:${HR_WEB_PORT:-6301}"
echo "Baza: 127.0.0.1:${HR_DB_PORT:-6332}, baza hr"
