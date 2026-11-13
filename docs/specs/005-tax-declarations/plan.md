# 005 Oświadczenia podatkowe — plan

[Specyfikacja](spec.md)

## Baza (`db/changelog`)

Schematy `tax_declarations` (tabela, funkcje wewnętrzne) i `tax_declarations_app` (kontrakt).

| Tabela | Kolumny (skrót) |
|---|---|
| `tax_declarations.declarations` | id, employee_id (FK, `ON DELETE CASCADE`), kind, share, year_from, year_to, submitted_at, valid_from (1. dzień miesiąca), valid_to (ostatni dzień miesiąca albo NULL), withdrawn_at (data złożenia wycofania), created_* — `EXCLUDE` nakładania wpisów tego samego rodzaju w kartotece (R5) |

Funkcje wewnętrzne (`tax_declarations`):

| Funkcja | R |
|---|---|
| `survives_contract_end(kind)` IMMUTABLE — `youthReliefWaiver`, `employeeCostsWaiver`, `authorCostsWaiver` | R1, R7 |
| `natural_end(kind, valid_from, year_to)` — 31 grudnia roku dla `noAdvances`, 31 grudnia `yearTo` dla `returnRelief`, inaczej NULL | R4 |
| `lapses_on(employee_id, kind, valid_from)` — koniec ciągłego okresu umów (wyspy z `employment.period`) | R7 |
| `status(valid_from, valid_to, lapses_on, on)` | R8 |
| `validate(kind, share, year_from, year_to, submitted_at, valid_from, valid_to)` → jsonb błędów pól | R2–R4 |
| `month_allowed(submitted_at, month)` — miesiąc złożenia albo następny | R3, R6 |
| `in_force(employee_id, on)` — wpisy obowiązujące w dniu (dla 009) | R11 |

Rutyny `tax_declarations_app`:

| Rutyna | Role | R |
|---|---|---|
| `declaration_list(employee_id, only_active)` | Kadry, Administrator; Pracownik tylko kartoteka powiązana z kontem | R10, R13 |
| `declaration_create(employee_id, kind, share, year_from, year_to, submitted_at, valid_from, valid_to)` INOUT id | Kadry, Administrator | R2–R5 |
| `declaration_end(id, row_version, withdrawn_at, stop_from)` — puste daty cofają zakończenie | Kadry, Administrator | R6 |
| `declaration_delete(id, row_version)` | Kadry, Administrator | R9 |

Dane deweloperskie (`context: dev`): PIT-2 1/12 „Anny Testowej” (R16).

## API

- `/api/employees/{id}/tax-declarations` (GET lista z `onlyActive`, POST nowy wpis).
- `/api/tax-declarations/{id}/end` (POST), `/api/tax-declarations/{id}` (DELETE z `rowVersion` w zapytaniu).
- Pracownik używa tego samego GET dla swojej kartoteki (baza sprawdza powiązanie).

## Front

- Kartoteka: zakładka „Oświadczenia” — lista (rodzaj, szczegóły, złożone, od, do, wygasa, status), przełącznik
  „tylko obowiązujące”, szuflada nowego wpisu (pola zależne od rodzaju, miesiąc od: złożenia albo następny,
  domyślnie następny), okno zakończenia, usunięcie.
- „Moje dokumenty”: sekcja „Oświadczenia podatkowe” w każdym profilu, tylko odczyt.
- Audyt: etykieta encji `tax_declaration`.

## Testy

- Baza: szczegóły zależnie od rodzaju, miesiąc od (złożenia, następny, inny), data złożenia z przyszłości, koniec
  `noAdvances` i `returnRelief`, nakładanie tego samego rodzaju (różne rodzaje mogą), zakończenie i cofnięcie,
  wygasanie po końcu umowy (ciągłość umowa o pracę → zlecenie, przerwa, wpis przed pierwszą umową, wnioski
  „obowiązujące”), status, `in_force`, usunięcie z wersją wiersza, audyt, Pracownik: własna kartoteka tak, cudza
  `U0004`, zapis `U0004`.
- HTTP: przepływ (utwórz, lista, zakończ, usuń), 400 z błędem pola, Pracownik czyta swoje i dostaje 403 przy zapisie.
- Front: `yarn check`, `yarn build`, test funkcji pomocniczych (miesiące do wyboru), przejście w przeglądarce.
