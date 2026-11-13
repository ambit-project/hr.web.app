# 007 Czas pracy i nieobecności — plan

[Specyfikacja](spec.md)

## Decyzje

- **Schematy** `time_absence` (tabele, funkcje wewnętrzne) i `time_absence_app` (kontrakt), moduł
  `db/changelog/modules/60-time-absence/`.
- **Kalendarz** = wiersze dni (`calendar_days`: company_id, date, kind, hours) — bez osobnej tabeli nagłówka; „rok ma
  kalendarz” = istnieją dni tej spółki w tym roku. Generowanie wstawia cały rok w jednej transakcji.
- **Święta** w `holidays` (date PK, name); dane 2026–2027 w migracji `main` (to fakty prawne, nie dane dev).
- **Rodzaje nieobecności** w `absence_types` (code PK, name, payment, limit_kind `none|leave|days`, limit_days,
  employment_only, sickness) — dane w migracji `main`, tylko do odczytu.
- **Dni i godziny nieobecności** nie są zapisywane w kolumnach — liczy je funkcja `absence_hours(...)` przy odczycie
  (z kalendarza i etatu), więc zmiana kalendarza albo etatu jest od razu widoczna w liście i w saldzie urlopu.
- **Limit urlopu** w `leave_entitlements` (employment_id, year UK, base_days 20|26, first_job, entitled_hours
  wyliczone albo poprawione, override_reason, carried_over_hours wyliczone albo poprawione). Propozycja liczona funkcją
  `leave_proposal(employment_id, year, base_days, first_job)`.
- **Ewidencja**: `overtime_entries` (employment_id, date, hours, rate 50|100) i `mandate_hours` (employment_id,
  period daterange, hours, confirmed_at, confirmation_form `written|electronic|documentary`).
- **Wersja wiersza** (`xmin`) przy zmianie i usuwaniu; audyt zmian jak w 005/006.

## Rutyny `time_absence_app`

| Rutyna | Role | R |
|---|---|---|
| `holiday_list(year)`, `holiday_add(date, name)`, `holiday_delete(date)` | odczyt: Kadry, Admin; zapis: Admin | R1 |
| `calendar_generate(company_id, year)` → liczba sobotnich świąt do wyznaczenia | Kadry, Admin | R2, R3 |
| `calendar_month(company_id, year, month)` — dni + ostrzeżenie o niewyznaczonym dniu | Kadry, Admin | R3 |
| `calendar_day_set(company_id, date, kind, hours)` | Kadry, Admin | R4 |
| `working_hours(employment_id, from, to)` (wewn.) i `employment_norm(employment_id, year, month)` | Kadry, Admin | R5 |
| `absence_type_list()` | wszyscy zalogowani | R6 |
| `absence_list(employment_id, year)`, `absence_create/update/delete` | Kadry, Admin | R7–R12 |
| `leave_entitlement_get(employment_id, year)` (propozycja + zapisane + saldo), `leave_entitlement_set(...)` | Kadry, Admin | R13–R15 |
| `my_leave_balance(employee_id, year)`, `my_absence_list(employee_id, year)` | Pracownik (swoja kartoteka) | R15, R19 |
| `overtime_list/add/delete` (+ suma roku i ostrzeżenie 150 h) | Kadry, Admin | R16 |
| `mandate_hours_list/add/update/delete` | Kadry, Admin | R17 |

## API

`/api/holidays`, `/api/companies/{id}/calendar/{year}` (POST generuj, GET miesiąc `?month=`),
`/api/companies/{id}/calendar/days/{date}` (PUT), `/api/absence-types`, `/api/employments/{id}/absences`,
`/api/absences/{id}`, `/api/employments/{id}/leave/{year}`, `/api/employments/{id}/overtime`, `/api/overtime/{id}`,
`/api/employments/{id}/mandate-hours`, `/api/mandate-hours/{id}`, `/api/employments/{id}/norm/{year}/{month}`,
`/api/my/{employeeId}/leave/{year}`.

## Front

- Menu „Czas pracy”: **Kalendarz** (spółka, rok; generowanie; siatka miesięcy z dniami, kliknięcie dnia zmienia
  rodzaj; ostrzeżenie o niewyznaczonym dniu za sobotnie święto) i **Święta** (Administrator).
- Strona umowy: sekcje **Nieobecności** (rok, lista, dialog), **Urlop** (propozycja, zapisany limit, saldo, dialog
  korekty), **Nadgodziny** (umowa o pracę) albo **Godziny zlecenia** (zlecenie godzinowe), **Wymiar** (miesiące roku).
- „Moje dokumenty”: saldo urlopu i nieobecności pracownika.

## Testy

Baza: generowanie kalendarza (dni robocze XI 2026 = 20, XII = 21, święto w sobotę), wyznaczenie dnia, wymiar × etat
(1/2), nieobecności: okres, nakładanie, zlecenie tylko choroba z chorobowym, limity UZ/OD/SW/UOP, saldo urlopu,
propozycja limitu (26 dni pełny rok, 1/2 etatu, od 15.03 → 22 dni, pierwsza praca), nadgodziny i ostrzeżenie 150 h,
godziny zlecenia bez nakładania, role i audyt. HTTP: przepływy i 403 Pracownika.
