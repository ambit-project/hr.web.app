# ADR-0006: Testy na prawdziwym PostgreSQL

- **Status:** Accepted
- **Data:** 2026-11-12

## Kontekst

Logika jest w bazie, więc testy bez bazy niczego nie dowodzą.

## Decyzja

- `tests/HrWebApp.Api.Tests`: Testcontainers uruchamia `postgres:18.6`, zakłada role skryptem
  `db/bootstrap/roles.sql` i migruje kontenerem Liquibase z `db/` — tak samo jak compose.
- Trzy poziomy: rutyny jako `hr_api` (`Database/`), handlery z DI (`Features/…HandlerTests`), HTTP przez
  `WebApplicationFactory` (`Features/…HttpTests`) oraz kontrakt (`Contract/`).
- Jedna baza na przebieg; testy nie sprzątają, tylko używają unikalnych nazw (`Unique.Name`).
- Front: Vitest dla czystej logiki (`src/**/*.test.ts`).

## Konsekwencje

Testy wymagają Dockera. Nie ma ich w hookach Git; uruchamia się je przy zmianie (filtr) i przed PR.
