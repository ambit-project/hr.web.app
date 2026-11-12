# Baza danych

PostgreSQL 18, migracje Liquibase 4.33 (ADR-0004). Logika danych jest w bazie (ADR-0003): zapis tylko procedurą,
odczyt tylko funkcją ze schematu `<moduł>_app`.

## Układ

| Ścieżka | Co | Jak działa |
|---|---|---|
| `bootstrap/roles.sql` | baza i role `<prefix>_owner`, `_migrator`, `_api` | uruchamia superużytkownik, idempotentne |
| `changelog.yaml` | korzeń changelogu | kolejność: `1-ddl` → `modules` → `9-grants` |
| `changelog/1-ddl/yyyymmdd-nn-<opis>.sql` | tabele, indeksy, schematy | changeset niezmienny, nowa zmiana = nowy plik |
| `changelog/2-data/yyyymmdd-nn-<opis>.sql` | dane (parametry prawne, kody ZUS, urzędy skarbowe; konta deweloperskie z `context:dev`) | changeset niezmienny, źródło w komentarzu |
| `changelog/modules/<nn>-<moduł>/<schemat>.<nazwa>.sql` | procedury i funkcje | `runOnChange:true`, jeden obiekt na plik, `CREATE OR REPLACE` |
| `changelog/9-grants/grants.sql` | prawa roli API | `runAlways:true` |

## Zasady

- Schematy modułu: `<moduł>` (tabele, tylko właściciel) i `<moduł>_app` (kontrakt dla API). Rola API ma tylko
  `USAGE` na `<moduł>_app` i `EXECUTE` na jego rutynach — żadnych praw do tabel.
- Rutyny kontraktu: `SECURITY DEFINER`, `SET search_path = pg_catalog, pg_temp`, nazwy w pełni kwalifikowane.
- Aktor: `platform.actor_id()` (ustawienie sesji `hr.actor_id`).
- Identyfikatory: `uuid` z `uuidv7()`; czas: `timestamptz`; kwoty: `numeric(12,2)`, zawsze PLN.
- Changeset w DDL: autor `hr`, id `yyyymmdd-nn` (data dnia pracy, kolejny numer).
- Rozszerzenia (`pgcrypto`, `btree_gist`) są w schemacie `ext`; rutyny wołają je z nazwą schematu.
- **Konteksty Liquibase:** `dev` = dane deweloperskie (compose, testy). Produkcja: `--contexts=main` — bez tego
  Liquibase uruchomi wszystkie changesety, także konta deweloperskie.

## Kody błędów (SQLSTATE)

| Kod | Znaczenie | HTTP | Szczegóły |
|---|---|---|---|
| `U0001` | walidacja | 400 | `HINT` = JSON `{"pole": ["kod", …]}`; kody pól: `required`, `maxLength`, `duplicate` |
| `U0002` | nie znaleziono (albo brak dostępu do rekordu) | 404 | |
| `U0003` | konflikt stanu | 409 | |
| `U0004` | brak uprawnień | 403 | |
| `U0005` | logowanie nieudane (zły login lub hasło, blokada, konto wyłączone) | 401 | zwraca API, nie baza; zawsze ten sam |
| `U0006` | brak parametru prawnego w dniu | 409 | `HINT` = `{"code": …, "on": …}` |

Kody pól w `U0001`: `required`, `maxLength`, `duplicate`, `invalid`, `beforeCurrent`, `tooShort`, `beforeFrom`
(data „do” przed „od”), `notFound` (wskazana pozycja słownika nie istnieje), `inactive` (pozycja nieaktywna).

Tłumaczenie na HTTP: `apps/api/HrWebApp.Api/Database/DbErrors.cs`. Front tłumaczy kody pól na teksty
(`apps/web/src/i18n/pl.ts`, sekcja `validation`).
