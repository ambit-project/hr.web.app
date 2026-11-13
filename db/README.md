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
- Klucz danych osobowych: `platform.data_key()` (ustawienie sesji `hr.data_key`, ADR-0009). PESEL, dokument
  i rachunek są w kolumnach `*_enc` (`pgp_sym_encrypt`), unikalność PESEL po `pesel_hmac`. **Na produkcji nie włączaj
  logowania parametrów zapytań** (`log_statement = all` z parametrami, `auto_explain.log_parameter_max_length`), bo
  klucz trafiłby do logu.
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
| `U0003` | konflikt stanu | 409 | opcjonalny `HINT` = powód (API: rozszerzenie `reason`): `retention` (okres przechowywania), `concurrentTitleUnsupported` (zlecenie/dzieło z własnym pracownikiem), `onlyVersion` (jedyna wersja warunków), `taxNumberMismatch` (NIP bazy Optimy inny niż spółki), `pending` / `succeeded` (ponowna próba operacji agenta, która czeka albo się udała), `calendarExists` (rok ma już kalendarz spółki), `holidaysMissing` (brak świąt roku w słowniku), `calendarMissing` (spółka nie ma kalendarza roku), `kindNotAllowed` (operacja nie dla tego rodzaju umowy, np. limit urlopu i nadgodziny tylko umowa o pracę, godziny tylko zlecenie), PPK (008): `alreadyEnrolled` (kartoteka ma już uczestnictwo), `alreadyResigned` (ostatnia deklaracja to rezygnacja, a nie ma informacji o ponownym zapisie w roku złożenia), `notResigned` (wniosek o wznowienie bez rezygnacji), `notEnrolled` (deklaracja stawki bez uczestnictwa), `notInformed` (wznowienie z cyklu bez daty informacji), `applied` (cykl już wykonany); bez powodu = nieaktualna wersja wiersza |
| `U0004` | brak uprawnień | 403 | |
| `U0005` | logowanie nieudane (zły login lub hasło, blokada, konto wyłączone) | 401 | zwraca API, nie baza; zawsze ten sam |
| `U0006` | brak parametru prawnego w dniu | 409 | `HINT` = `{"code": …, "on": …}` |

Kody pól w `U0001`: `required`, `maxLength`, `duplicate`, `invalid`, `beforeCurrent`, `tooShort`, `beforeFrom`
(data „do” przed „od”), `notFound` (wskazana pozycja słownika nie istnieje), `inactive` (pozycja nieaktywna),
`mismatch` (data urodzenia inna niż w PESEL), `afterStart` (zawarcie po początku umowy), `overlap` (nakładanie się
umów o pracę albo oświadczeń podatkowych tego samego rodzaju), `beforeTermination`, `termsOutsidePeriod` (wersja warunków poza okresem umowy), `outsidePeriod`,
`missingMandatory` (brak obowiązkowego ubezpieczenia), `belowMinimum` (poniżej płacy minimalnej lub stawki
godzinowej), `beforeStart`, `afterEnd`, `future` (data z przyszłości), `outsideAllowed` (miesiąc spoza
dozwolonych: oświadczenie działa od miesiąca złożenia albo następnego), `beforeSubmission` (wycofanie przed złożeniem), `notLinked` (spółka bez powiązania z bazą Optimy), `kindMismatch`
(rodzaj płacy Optimy i składnik różnego rodzaju), `taxNumberMismatch` (baza Optimy zgłoszona z innym NIP), `mismatch`
(wynik agenta dla innej bazy lub miesiąca niż operacja), `payload` (`invalid` — zły kształt wyniku agenta). Czas pracy (007): `limitExceeded` (przy `dateTo`: limit dni
nieobecności w roku albo na zdarzenie; dodatkowy klucz `remainingDays` = `["<dni>"]`), `leaveLimitExceeded` (przy
`dateTo`: urlop ponad saldo; dodatkowy klucz `remainingHours` = `["<godziny>"]`). PPK (008): `notEmployed` (w dniu nie jest osobą
zatrudnioną w rozumieniu ustawy o PPK), `tooEarly` (zapis przed upływem 14 dni zatrudnienia), `tooYoung` (poniżej 18 lat),
`tooOld` (70 lat i więcej), `requestMissing` (55–69 lat bez wniosku o zawarcie umowy złożonego najpóźniej w dniu
zapisu), `notCycleYear` (rok bez dat ponownego zapisu w parametrach `ppk.ponowny.zapis.*`).

Tłumaczenie na HTTP: `apps/api/HrWebApp.Api/Database/DbErrors.cs`. Front tłumaczy kody pól na teksty
(`apps/web/src/i18n/pl.ts`, sekcja `validation`).
