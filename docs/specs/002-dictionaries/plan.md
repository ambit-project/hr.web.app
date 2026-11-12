# 002 Słowniki — plan

[Specyfikacja](spec.md)

## Baza (`db/changelog`)

| Tabela | Kolumny (skrót) |
|---|---|
| `dictionaries.positions` | bez zmian; wersja wiersza z `xmin` |
| `dictionaries.zus_codes` | id, kind (`title`, `pension`, `disability`), code, name, valid_from, valid_to, created_at, created_by — UK (kind, code); CHECK formatu kodu dla rodzaju i `valid_to >= valid_from` |
| `dictionaries.tax_offices` | id, code (UK, 4 cyfry), name, is_active, created_at, created_by |
| `platform.companies` | + `tax_office_id` (FK → `tax_offices`, NULL dla spółek sprzed 002; wymagane przez rutyny) |

Changesety: DDL `20261112-08` (tabele słowników), `20261112-09` (kolumna spółki); dane `20261112-10` (kody ZUS),
`20261112-11` (urzędy skarbowe). Dane bez kontekstu `dev` — trafiają też na produkcję.

Rutyny `dictionaries_app` (rola API: `EXECUTE`), `SECURITY DEFINER`:

| Rutyna | Kto | R |
|---|---|---|
| `position_list` (+ `row_version`), `position_update(id, row_version, name, description, is_active)` | Kadry, Administrator | R1–R2 |
| `zus_code_list(kind, search, on_date, limit, offset)` | Kadry, Administrator | R5 |
| `zus_code_create(kind, code, name, valid_from, valid_to)`, `zus_code_update(id, row_version, name, valid_from, valid_to)` | Administrator | R6 |
| `tax_office_list(search, include_inactive, limit, offset)` | Kadry, Administrator | R9 |
| `tax_office_create(code, name)`, `tax_office_update(id, row_version, name, is_active)` | Administrator | R9 |

Wewnętrzne: `dictionaries.zus_code_valid(kind, code, on_date) → boolean` (R7).

Spółki (`platform_app.company_list/create/update`): parametr i kolumny urzędu (`tax_office_id`, `tax_office_code`,
`tax_office_name`). Walidacja w `platform.company_validate`: `taxOfficeId` `required`, `notFound`, `inactive`
(nieaktywny dozwolony, gdy spółka już go ma). Zmiana listy parametrów rutyny = `DROP` starej sygnatury w tym samym
changesecie, inaczej powstaje przeciążenie.

## API

| Endpoint | Rutyna |
|---|---|
| `PUT /api/positions/{id}` | `position_update` |
| `GET /api/zus-codes?kind&search&onDate&page&pageSize`, `POST /api/zus-codes`, `PUT /api/zus-codes/{id}` | `zus_code_*` |
| `GET /api/tax-offices?search&includeInactive&page&pageSize`, `POST /api/tax-offices`, `PUT /api/tax-offices/{id}` | `tax_office_*` |
| `/api/companies` | + `taxOfficeId` w żądaniach, `taxOfficeId`, `taxOfficeCode`, `taxOfficeName` w liście |

`kind` w API to tekst z wartościami `title`, `pension`, `disability` (bez enum C#, reguły zna baza).

## Front

- Menu: grupa „Słowniki” (Kadry, Administrator): Stanowiska, Kody ZUS, Urzędy skarbowe.
- Stanowiska: edycja w szufladzie, przełącznik „pokaż nieaktywne” w URL.
- Kody ZUS: lista z filtrem rodzaju (`Select`), wyszukiwaniem i dniem w URL; szuflada dodawania i edycji tylko
  dla Administratora.
- Urzędy skarbowe: lista z wyszukiwaniem i „pokaż nieaktywne”; szuflada tylko dla Administratora.
- Spółka: pole urzędu jako `ComboBox` z wyszukiwaniem po stronie serwera (aktywne urzędy).

## Testy

- Baza: role i audyt każdej rutyny zapisu, formaty kodów, `valid_to < valid_from`, unikalność, wersja wiersza,
  `zus_code_valid` na granicach okresu, wyszukiwanie urzędów, walidacja urzędu spółki (`required`, `inactive`,
  zachowanie nieaktywnego przy edycji).
- Dane: kody `0110`, `0411` i urzędy są w bazie po migracji.
- HTTP: 403 dla Kadr przy zapisie kodu i urzędu, edycja stanowiska z 409.
- Front: `yarn check`, `yarn build`, przejście w przeglądarce.
