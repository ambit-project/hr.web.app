# ADR-0002: Stack i układ repozytorium

- **Status:** Accepted
- **Data:** 2026-11-12

## Kontekst

Kadry i Płace zgodne z polskim prawem, kwoty w PLN. Repo jest publiczne (MIT), więc każdy ma je uruchomić
lokalnie bez naszej infrastruktury.

## Decyzja

- Monorepo: `apps/api` (.NET 11, ASP.NET Core Minimal API), `apps/web` (TanStack Start + Router + Query + Form,
  HeroUI v3, Tailwind v4, Yarn 4, Biome), `db/` (PostgreSQL 18, Liquibase), `tests/` (xUnit),
  `infrastructure/` (docker compose), `docs/` (ADR, specyfikacje SDD).
- Nazwy: solucja i projekty `HrWebApp.*`, role bazy z prefiksem `hr_`, baza `hr`.
- Narzędzia .NET w manifeście `.config/dotnet-tools.json` (CSharpier, Husky.Net); hooki: pre-commit
  (`csharpier check`, build), pre-push (build, `jb inspectcode`). Testy uruchamia się ręcznie.
- Zależności z licencją zgodną z MIT; licencje komercyjne lub copyleft tylko po decyzji właściciela.
- Porty lokalne: API 6300, front 6301, PostgreSQL 6332 (na 127.0.0.1).

## Konsekwencje

Nowy `ProjectReference` w API wymaga dopisania `.csproj` do warstwy restore w `Dockerfile.api`.
