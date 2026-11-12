# hr.web.app

Kadry i Płace zgodne z polskim prawem (kwoty w PLN): pracownicy, zatrudnienie, listy płac, paski, PIT.
Gotowa jest platforma (moduł 001): logowanie i role, spółki, parametry prawne 2026, użytkownicy, audyt zmian oraz
słownik stanowisk. Produkt: `PRODUCT.md`, model domeny: `docs/domain/`, specyfikacje: `docs/specs/`, decyzje:
`docs/adr/`.

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

W Development strona logowania ma przyciski „Zaloguj jako Kadry / Administrator / Pracownik” (konta deweloperskie
bez hasła).

## Praca nad kodem

```bash
infrastructure/start.sh --db-only               # tylko baza + migracje
dotnet run --project apps/api/HrWebApp.Api      # API na :6300 (appsettings.Development.json)
cd apps/web && yarn install && yarn dev         # front na :6301 (HR_API_URL, domyślnie :6300)
```

Po zmianie w `db/` uruchom ponownie migracje: `docker compose -f infrastructure/docker-compose.yml up migrate`.

## Konfiguracja API (zmienne środowiskowe)

| Zmienna | Domyślnie | Znaczenie |
|---|---|---|
| `ConnectionStrings__Hr` | — | połączenie jako rola `hr_api` (wymagane) |
| `Auth__SessionHours` | 12 | ważność sesji |
| `Auth__MaxFailedAttempts` / `Auth__LockoutMinutes` | 5 / 15 | blokada po nieudanych próbach logowania |
| `Auth__MinPasswordLength` | 12 | minimalna długość hasła nadawanego przez Administratora |
| `Auth__DevLogin` | false | logowanie jednym kliknięciem; działa tylko w `Development` |
| `Auth__BootstrapAdmin__Login`, `__Email`, `__Password`, `__DisplayName` | — | pierwszy Administrator przy starcie, gdy żadnego nie ma |
| `Data__EncryptionKey` | — (wymagany) | klucz szyfrowania PESEL i rachunku, co najmniej 32 znaki ([ADR-0009](docs/adr/0009-personal-data-encryption.md)); utrata = utrata tych danych. Compose: `HR_DATA_KEY` w `infrastructure/.env`, przy `dotnet run` ustaw zmienną sam |

Front: `HR_API_URL` (adres API widziany z serwera frontu, domyślnie `http://localhost:6300`).
Migracje produkcyjne: Liquibase z `--contexts=main` (bez kont deweloperskich).

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
