# Stawia lokalny stos: PostgreSQL 18, role, migracje, API i front.
# Użycie: infrastructure\start.ps1 [-DbOnly]
param([switch]$DbOnly)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

if (-not (Test-Path .env)) {
    Copy-Item .env-sample .env
    Write-Host "Utworzono infrastructure/.env z .env-sample (wartości deweloperskie)."
}

if ($DbOnly) {
    docker compose up -d --wait postgres
    docker compose up db-init migrate --exit-code-from migrate
} else {
    docker compose up -d --build --wait
}
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "API:  http://127.0.0.1:6300 (OpenAPI: /openapi/v1.json, Scalar: /scalar)"
Write-Host "Web:  http://127.0.0.1:6301"
Write-Host "Baza: 127.0.0.1:6332, baza hr"
