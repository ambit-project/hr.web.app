# 004 Zatrudnienie — plan

[Specyfikacja](spec.md)

## Baza (`db/changelog`)

Schematy `employment` (tabele) i `employment_app` (kontrakt).

| Tabela | Kolumny (skrót) |
|---|---|
| `employment.employments` | id, employee_id (FK, `ON DELETE CASCADE`), company_id (FK, kopia ze spółki kartoteki), number (UK w spółce), kind, contract_type, signed_at, start_date, end_date, terminated_at, created_* — `EXCLUDE` nakładania umów o pracę kartoteki |
| `employment.terms` | id, employment_id (FK, `CASCADE`), valid_from, valid_to, position_id (FK), fte_numerator, fte_denominator, pay_basis, base_amount numeric(12,2), zus_title_code_id, zus_pension_code_id, zus_disability_code_id (FK → `zus_codes`), insurances text[], cost_kind, created_* — `EXCLUDE` nakładania wersji |

Okres umowy: `daterange(start_date, coalesce(terminated_at, end_date), '[]')` — funkcja `employment.period(...)`
używana w `EXCLUDE`, w regule zbiegu (R5) i w statusach.

Funkcje wewnętrzne (`employment`):

| Funkcja | R |
|---|---|
| `period(start, end, terminated)` IMMUTABLE | R4, R5, R8 |
| `status(start, end, terminated, on)` | R8 |
| `retention_until(kind, start, end, terminated)` | R17 |
| `employment_validate(...)` → jsonb błędów pól | R1–R4 |
| `terms_validate(kind, ...)` → jsonb; kody ZUS → id | R10–R13 |
| `check_concurrency(employee_id, kind, period, except_id)` → `U0003` z HINT `concurrentTitleUnsupported` | R5 |
| `employee_status(employee_id, on)`, `employee_retention_until(employee_id)` | R16, R17 |

Rutyny `employment_app` (Kadry, Administrator):

| Rutyna | R |
|---|---|
| `employment_list(employee_id)` — umowy z bieżącą wersją, statusem i okresem przechowywania | R8 |
| `employment_get(id)` — umowa z wersją wiersza; zapisuje `read_audit` | R19 |
| `terms_list(employment_id)` — wszystkie wersje z kodami i nazwą stanowiska | R14 |
| `employment_create(employee_id, number, kind, contract_type, signed_at, start_date, end_date, <warunki>)` INOUT id | R1–R5, R10–R13 |
| `employment_update(id, row_version, number, contract_type, signed_at, start_date, end_date)` | R6 |
| `employment_terminate(id, row_version, terminated_at)` | R7 |
| `employment_delete(id)` | R9 |
| `terms_add(employment_id, valid_from, <warunki>)` INOUT id | R10–R15 |
| `terms_delete_last(employment_id)` | R14 |

Zmiany w 003: `employees_app.employee_list` dostaje `p_status` i kolumnę `status`; `employee_get` — `status`
i `retention_until`; `employee_delete` sprawdza okres przechowywania (R18). Zmiana sygnatur = `DROP` starej w tym
samym changesecie.

`platform.legal_parameter_or_null(code, on)` — wartość albo NULL (R13: brak parametru nie blokuje umów historycznych).

Dane deweloperskie (`context: dev`): stanowisko „Specjalista (dev)” i umowa o pracę „Anny Testowej” (R21).

## API

- `DbErrorHandler`: dla błędów innych niż `U0001` niepusty `HINT` trafia do rozszerzenia `reason` (np.
  `concurrentTitleUnsupported`, `retention`) — front tłumaczy powód po kodzie.
- `/api/employees/{id}/employments` (GET lista, POST nowa umowa z pierwszą wersją warunków).
- `/api/employments/{id}` (GET szczegóły z wersjami, PUT, DELETE), `/api/employments/{id}/terminate` (POST),
  `/api/employments/{id}/terms` (POST), `/api/employments/{id}/terms/last` (DELETE).
- `ListEmployees` — parametr `status`, kolumna `status`; `GetEmployee` — `status`, `retentionUntil`.

## Front

- Kartoteka: zakładka „Umowy” (lista umów: numer, rodzaj, okres, status, bieżące warunki) i szuflada nowej umowy
  (rodzaj → pola warunków zależne od rodzaju, zestawy ubezpieczeń). Na zakładce „Dane” status i „przechowywać do”.
- `/employments/$id`: strona umowy — dane (edycja, rozwiązanie, usunięcie), historia wersji warunków, szuflada nowej
  wersji (wartości z ostatniej), usunięcie ostatniej wersji.
- Lista kartotek: kolumna i filtr statusu.
- `ZusCodePicker` (ComboBox z wyszukiwaniem tytułów obowiązujących) i `Select` dla kodów emerytury i niepełnosprawności.
- Komunikaty `reason` z 409 po kodzie.

## Testy

- Baza: numer automatyczny i duplikat, daty (R3), nakładanie umów o pracę, zbieg zlecenia i dzieła z umową o pracę
  w obie strony i brak zbiegu dzień po rozwiązaniu, pola warunków zależnie od rodzaju, kody ZUS, ubezpieczenia,
  płaca minimalna z etatem i stawka godzinowa, wersje (zamknięcie poprzedniej, `beforeCurrent`, `outsidePeriod`,
  poprawka tej samej daty, usunięcie ostatniej), edycja początku przesuwa pierwszą wersję, rozwiązanie, status
  i okres przechowywania (2019+, przed 2019, otwarta), usunięcie kartoteki w okresie → `U0003`, po okresie → OK,
  audyt odczytu, role.
- HTTP: przepływ umowy (utwórz, odczytaj, nowa wersja, rozwiąż, usuń), 409 z `reason`, Pracownik 403.
- Front: `yarn check`, `yarn build`, test funkcji pomocniczych (zestawy ubezpieczeń), przejście w przeglądarce.
