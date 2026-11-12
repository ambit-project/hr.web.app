# hr.web.app

Kadry i Płace zgodne z polskim prawem (kwoty w PLN): pracownicy, zatrudnienie, listy płac, paski, PIT.
Projekt jest na etapie szkieletu — działa słownik stanowisk przez wszystkie warstwy. Decyzje: `docs/adr/`.

## Stack

- API: .NET 11, ASP.NET Core Minimal API, czysty Npgsql (`apps/api`)
- Logika: PostgreSQL 18, procedury i funkcje, migracje Liquibase 4.33 (`db/`)
- Front: TanStack Start + Router + Query + Form, HeroUI v3, Tailwind v4, Yarn 4, Biome (`apps/web`)
- Testy: xUnit + Testcontainers na prawdziwym PostgreSQL (`tests/`), Vitest (`apps/web`)

## Wymagania

- .NET SDK 11 (`global.json`), Node.js 22.12+ z Corepack (`corepack enable`), Docker

## Uruchomienie całości w Dockerze

```bash
infrastructure/start.sh          # Windows: infrastructure\start.ps1
```

Tworzy `infrastructure/.env` z `.env-sample` (wartości deweloperskie), stawia PostgreSQL, zakłada role, migruje
i uruchamia API oraz front:

- front: http://127.0.0.1:6301
- API: http://127.0.0.1:6300 (OpenAPI `/openapi/v1.json`, Scalar `/scalar`, `/health`)
- baza: `127.0.0.1:6332`, baza `hr`

## Praca nad kodem

```bash
infrastructure/start.sh --db-only               # tylko baza + migracje
dotnet run --project apps/api/HrWebApp.Api      # API na :6300 (appsettings.Development.json)
cd apps/web && yarn install && yarn dev         # front na :6301 (HR_API_URL, domyślnie :6300)
```

Po zmianie w `db/` uruchom ponownie migracje: `docker compose -f infrastructure/docker-compose.yml up migrate`.

## Build, testy, format

```bash
dotnet build
dotnet test                                     # wymaga Dockera (Testcontainers)
dotnet test --filter "FullyQualifiedName~Positions"
dotnet csharpier check .                        # format: dotnet csharpier format .
scripts/inspect-code.sh                         # JetBrains inspectcode (narzędzie globalne jb)

cd apps/web
yarn check && yarn typecheck && yarn test && yarn build
```

## Kontrakt API i klient frontu

Endpointy są pisane ręcznie; klient TypeScript generuje orval z migawki `apps/web/openapi/hr-v1.json` (ADR-0005):

```bash
UPDATE_OPENAPI_SNAPSHOT=1 dotnet test --filter OpenApiSnapshotTests
cd apps/web && yarn api:gen
```

## Git hooks (Husky.Net)

`dotnet restore` instaluje hooki. pre-commit: `csharpier check`, `dotnet build`; pre-push: build i
`scripts/inspect-code.sh`. Testy uruchamia się ręcznie. Wyłączenie: `HUSKY=0`.

## Licencja

MIT — `LICENSE`.
