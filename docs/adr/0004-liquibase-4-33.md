# ADR-0004: Liquibase 4.33 (Apache-2.0), nie 5.x

- **Status:** Accepted
- **Data:** 2026-11-12

## Kontekst

Liquibase od 5.0 jest wydawany na licencji Functional Source License (FSL-1.1-ALv2), która nie jest licencją open
source. Repo jest publiczne na MIT i ma działać bez licencji komercyjnych.

## Decyzja

Używamy obrazu `liquibase/liquibase:4.33.0` (ostatnia linia na Apache-2.0) w compose i w testach. Changesety
w formacie „formatted SQL”. Parametr changelogu `api_role` (rola API) podaje się przy `update` (`-Dapi_role=hr_api`).

## Konsekwencje

Aktualizacja do 5.x wymaga nowego ADR z oceną licencji. Wersja obrazu jest w dwóch miejscach:
`infrastructure/docker-compose.yml` i `tests/HrWebApp.Api.Tests/Infrastructure/DatabaseFixture.cs`.
