# 008 PPK — plan

[Specyfikacja](spec.md)

## Decyzje

- Schematy `ppk` i `ppk_app`, moduł `db/changelog/modules/70-ppk/`.
- `company_settings` (company_id, valid_from — 1. dzień miesiąca, institution_name, agreement_number, agreement_date,
  employer_additional_rate numeric(4,2) 0–2,5) — `EXCLUDE` nakładania okresów spółki.
- `participations` (employee_id UK, enrolled_at, institution_member_number) — jedno uczestnictwo na kartotekę
  (kartoteka = spółka); wypisanie się nie kasuje uczestnictwa (rezygnacja jest deklaracją).
- `rate_periods` (participation_id, valid_from, valid_to, employee_basic_rate 0,5–2, employee_additional_rate 0–2) —
  `EXCLUDE`; stawki pracodawcy liczone z `company_settings` w dniu.
- `declarations` (employee_id, kind, submitted_at, rate — dla reducedRate/additionalRate, effective_from wyliczane:
  rezygnacja = 1. dzień miesiąca złożenia, pozostałe = 1. dzień następnego) — niezmienne, usuwanie tylko pomyłek.
- `reenrollment_cycles` (company_id, year, informed_at) i wynik wznowienia w `declarations` jako `autoRestart`
  (rodzaj systemowy, effective_from = 1.04 roku cyklu).
- Parametry prawne cyklu dopisane do `platform.legal_parameters` (dane `main`): daty 2027-02-28 i 2027-04-01.
- Funkcje wewnętrzne: `employment_months(employee_id, on)` (3 miesiące w 12 wstecz, R3), `is_employed_person(...)`
  (R2), `age_on`, `rates_on(employee_id, on)` → stawki pracownika i pracodawcy albo zera (R11), `status_on`.

## Rutyny `ppk_app`

`company_settings_list/set`, `enrollment_candidates(company_id, on)` (R5), `participation_get(employee_id)`,
`participation_create(employee_id, enrolled_at, member_number, basic_rate, additional_rate)` (R6, R7),
`declaration_list/create/delete(employee_id)` (R9, R10), `reenrollment_list(company_id, year)`,
`reenrollment_mark_informed(company_id, year, informed_at)`, `reenrollment_apply(company_id, year)` (R12, R13),
`my_ppk(employee_id)` (R14).

## API

`/api/companies/{id}/ppk/settings` (GET, POST nowy okres), `/api/companies/{id}/ppk/candidates?on=`,
`/api/employees/{id}/ppk` (GET uczestnictwo + stawki + deklaracje + status; POST zapis),
`/api/employees/{id}/ppk/declarations` (POST), `/api/ppk/declarations/{id}` (DELETE `?rowVersion`),
`/api/companies/{id}/ppk/reenrollment/{year}` (GET, POST informed, POST apply), `/api/my/{employeeId}/ppk`.

## Front

- Menu „Kadry → PPK”: wybór spółki; zakładki **Ustawienia** (okresy umowy o zarządzanie i wpłaty dodatkowej
  pracodawcy), **Do zapisu** (lista R5 z terminami i statusami, przycisk zapisu otwiera dialog), **Ponowny zapis**
  (rok cyklu, lista R13, „informacja przekazana”, „wznów wpłaty”).
- Kartoteka: zakładka **PPK** — status, stawki (historia), deklaracje (dialog z rodzajem, datą, stawką).
- „Moje dokumenty”: status, stawki, deklaracje.

## Testy

Baza: osoba zatrudniona (umowa o pracę, zlecenie z/bez emerytalnego, dzieło), 3 miesiące w 12 wstecz z przerwą
i dwoma tytułami, termin zapisu (10. dzień), wiek 18/55/70, wniosek 55+, rezygnacja od miesiąca złożenia, wznowienie
od następnego, obniżona i dodatkowa od następnego, `rates_on` z rezygnacją, cykl 2027 (podział do 55 / 55–69 / 70+,
apply), ustawienia spółki bez nakładania, role i audyt. HTTP: przepływy i 403 Pracownika.
