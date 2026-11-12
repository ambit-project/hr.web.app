# ADR-0005: Ręczne endpointy, OpenAPI z kodu, klient orval

- **Status:** Accepted
- **Data:** 2026-11-12

## Kontekst

Kontrakt HTTP nie jest generowany z katalogu bazy. Front potrzebuje typowanego klienta.

## Decyzja

- Endpointy Minimal API pisane ręcznie, po funkcjach: `Features/<Obszar>/<Operacja>.cs` (request, handler,
  odpowiedź) i `<Obszar>Endpoints.cs` (grupa `/api/<obszar>`, tag, rejestracja handlerów).
- `Microsoft.AspNetCore.OpenApi` buduje dokument OpenAPI 3.1 (`/openapi/v1.json`, tylko w Development; Scalar
  pod `/scalar`). Nazwy schematów z typem zewnętrznym (`ListPositionsItem`), liczby bez wariantu „string”.
- Migawka dokumentu `apps/web/openapi/hr-v1.json` jest w repo. Test `OpenApiSnapshotTests` pilnuje zgodności;
  aktualizacja: `UPDATE_OPENAPI_SNAPSHOT=1 dotnet test --filter OpenApiSnapshotTests`, potem `yarn api:gen`.
- orval generuje gołe funkcje axios (`src/api/endpoints`, `src/api/models` — nie edytować). Przeglądarka nie woła
  API: woła server functions TanStack Start (`features/<obszar>/<obszar>.functions.ts`), które wołają API
  (`HR_API_URL`). Błędy 4xx wracają z server function jako dane (`Result`), nie jako wyjątek.

## Konsekwencje

Generowanie klienta nie wymaga działającego API. Zmiana kontraktu bez odświeżenia migawki psuje test.
