# 007 Czas pracy i nieobecności — zadania

[Specyfikacja](spec.md) · [Plan](plan.md)

- [x] T1. Baza: schematy `time_absence` / `time_absence_app`, tabele (święta, dni kalendarza, rodzaje nieobecności,
      nieobecności z `EXCLUDE`, limity urlopu, nadgodziny, godziny zlecenia z `EXCLUDE`), święta 2026–2027 i rodzaje
      nieobecności (`main`), dane deweloperskie (R20), funkcje wewnętrzne (`working_hours`, `work_days`,
      `month_summary`, `leave_proposal`, `leave_entitled`, `leave_carried`, `leave_used`, walidacja i limity
      nieobecności), rutyny kontraktu, uprawnienia. Testy rutyn.
- [x] T2. API: endpointy świąt, kalendarza, wymiaru, nieobecności, limitu urlopu, nadgodzin, godzin zlecenia
      i „moich” danych; migawka OpenAPI. Testy HTTP.
- [ ] T3. Front: menu „Czas pracy” (Kalendarz, Święta), sekcje umowy (Nieobecności, Urlop, Nadgodziny / Godziny
      zlecenia, Wymiar), „Moje dokumenty” (saldo, nieobecności), etykiety audytu i kody błędów.
- [ ] T4. Dokumentacja: PRODUCT, sprawdzenie w przeglądarce.

## Decyzje przy implementacji (różnice względem planu)

- Kalendarz: `GET /api/companies/{id}/calendar/{year}` zwraca podsumowanie 12 miesięcy (`calendar_year`: dni robocze,
  godziny, święta w sobotę, wyznaczone dni, ostrzeżenie), a dni miesiąca — `GET …/calendar/{year}/{month}`
  (`calendar_month`) zamiast `?month=` — dwa różne kształty odpowiedzi.
- Wymiar: `GET /api/employments/{id}/norm/{year}?month=` zwraca listę miesięcy (pusty `month` — cały rok), zamiast
  `/norm/{year}/{month}` — sekcja „Wymiar” pokazuje miesiące roku jednym zapytaniem.
- „Moje”: `GET /api/my/{employeeId}/leave/{year}` i dodatkowo `GET /api/my/{employeeId}/absences/{year}`
  (`my_absence_list` z planu nie miał ścieżki).
- Rodzaj nieobecności: kluczem jest kod (`absence_types.code`, jak w planie); pole API nazywa się `absenceTypeId`
  (jak w R8 specyfikacji) i niesie kod, np. `UW`.
- Limity: `limit_kind` ma też `yearDays` (OD, SW, UOP — dni w roku) i `event` (UO — 2 dni robocze na jedną
  nieobecność), bo „days” z planu nie odróżniał limitu rocznego od limitu na zdarzenie. UZ: `leave` + 4 dni w roku.
  Liczba pozostała idzie w `U0001` dodatkowym kluczem `remainingDays` / `remainingHours`.
- Godziny zlecenia: kolumny `date_from` / `date_to` zamiast `daterange` (jak nieobecności; `EXCLUDE` na zakresie).
- Limit urlopu: brak zapisanego limitu roku = 0 h (urlop `leaveLimitExceeded`), bo wymiar 20/26 dni wybierają Kadry.
  Etat do propozycji — z pierwszego dnia umowy w roku (zmiana etatu w trakcie roku — korekta Kadr, R14). Zaległy
  wyliczany, gdy poprzedni rok ma zapisany limit.
- Nieobecność umowy o pracę wymaga kalendarza spółki we wszystkich latach okresu (409 `calendarMissing`); zlecenie
  (tylko choroba) — nie, godziny pracy zlecenia = 0.
- `designatedOff` tylko pn–pt i nie więcej niż świąt w sobotę w tym samym miesiącu; `holiday` tylko dla dat ze
  słownika. Dodanie lub usunięcie święta nie zmienia wygenerowanych kalendarzy.

## Mapa wymaganie → test

Testy rutyn: `tests/HrWebApp.Api.Tests/Database/TimeAbsenceRoutineTests.cs`; HTTP:
`tests/HrWebApp.Api.Tests/Features/TimeAbsence/TimeAbsenceHttpTests.cs`.

| R | Test |
|---|---|
| R1 | `Roles_admin_writes_holidays_and_pracownik_reads_only_own_record` (dodanie, duplikat, usunięcie, 403 Kadr), `Generated_calendar_…` (24.12 święto) |
| R2 | `Generated_calendar_has_2026_norms_and_cannot_be_generated_twice` (`calendarExists`, `holidaysMissing`), `TimeAbsenceHttpTests.Kadry_generate_calendar_…` |
| R3 | `Generated_calendar_…` (2 święta w sobotę, XII 168 h z ostrzeżeniem), `Designated_day_off_lowers_the_month_and_holidays_stay_holidays` |
| R4 | `Designated_day_off_…` (święto nie jest dniem roboczym, godziny 0–12, audyt) |
| R5 | `Norm_is_calendar_hours_in_contract_times_fte` (XI 2026 1/2 etatu = 80 h, od 16.11 = 88 h, `calendarMissing`, `kindNotAllowed`), HTTP `norm?month=11` = 160 h |
| R6 | HTTP `Pracownik_reads_own_…` (11 rodzajów, odczyt dla każdej roli) |
| R7 | `Absence_stays_in_contract_does_not_overlap_and_counts_days_and_hours` (`outsidePeriod`, `overlap`, e-ZLA wymagany, `notFound`) |
| R8 | `Mandate_allows_only_sickness_with_sickness_insurance_and_specific_work_nothing` |
| R9 | `Absence_stays_…` (choroba 30.11–2.12: 3 dni kalendarzowe, 18 h przy 3/4 etatu; `working_hours` dla 009) |
| R10 | `Yearly_and_per_event_limits_count_work_days` (UZ 4, OD 2, SW 2, UOP 5, UO 2 na zdarzenie, weekend się nie liczy) |
| R11 | `Leave_cannot_exceed_balance_and_update_and_delete_need_row_version` (`leaveLimitExceeded` z `remainingHours`) |
| R12 | `Leave_cannot_exceed_balance_…` (wersja wiersza, zmiana, usunięcie), HTTP usunięcie z `rowVersion` |
| R13 | `Leave_proposal_follows_fte_months_and_first_job` (26 dni = 208 h, 1/2 → 104 h, 3/4 × 20 → 120 h, od 15.03 → 22 dni, pierwsza praca → 15 dni, VI–XII → 12 dni) |
| R14 | `Unused_leave_carries_over_and_override_needs_reason` (zaległy 96 h, uzasadnienie korekty, wersja wiersza); `Leave_cannot_exceed_…` (korekta limitu) |
| R15 | `Unused_leave_…` (saldo 38 dni), HTTP `Kadry_generate_…` (saldo 21 dni), `Pracownik_reads_own_…` (saldo 184 h = 23 dni) |
| R16 | `Overtime_is_for_employment_and_warns_above_150_hours`, HTTP `Kadry_record_overtime_and_mandate_hours` |
| R17 | `Mandate_hours_do_not_overlap_and_confirmation_needs_date_and_form`, HTTP `Kadry_record_overtime_…` |
| R18 | `ON DELETE CASCADE` z umowy (kartoteka znika z ewidencją; okres przechowywania z 004 R17) — bez osobnego testu |
| R19 | audyt: `Designated_day_off_…`, `Leave_cannot_exceed_…`; role: `Roles_admin_writes_holidays_…`, HTTP `Pracownik_reads_own_leave_and_absences_and_gets_403_elsewhere` |
| R20 | `Roles_admin_writes_holidays_…` i HTTP `Pracownik_reads_own_…` (Anna Testowa: 26 dni, urlop 3 dni, choroba 5 dni) |
