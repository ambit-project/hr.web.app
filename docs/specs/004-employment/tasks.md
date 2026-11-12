# 004 Zatrudnienie — zadania

[Specyfikacja](spec.md) · [Plan](plan.md)

- [x] T1. Baza: tabele, funkcje wewnętrzne, rutyny umów i wersji, zmiany w rutynach kartoteki, dane deweloperskie.
      Testy rutyn.
- [x] T2. API: `reason` w ProblemDetails, endpointy umów, status i retencja kartoteki. Testy HTTP, migawka OpenAPI.
- [x] T3. Front: zakładka „Umowy”, szuflady umowy i wersji, strona umowy, status na liście.
- [x] T4. Dokumentacja: kody błędów i powodów, PRODUCT, mapa wymaganie → test.

## Mapa wymaganie → test

Testy rutyn: `tests/HrWebApp.Api.Tests/Database/EmploymentRoutineTests.cs`; HTTP:
`tests/HrWebApp.Api.Tests/Features/Employments/EmploymentHttpTests.cs`.

| R | Test |
|---|---|
| R1, R2 | `Empty_number_gets_next_number_and_duplicate_is_rejected`, `EmploymentHttpTests.Kadry_create_read_change_terms_terminate_and_delete_a_contract` |
| R3 | `Dates_are_checked_by_contract_type` |
| R4 | `Employment_contracts_of_one_employee_do_not_overlap` |
| R5 | `Mandate_or_work_with_own_employee_is_rejected_both_ways_but_allowed_after_termination`, `EmploymentHttpTests.Mandate_with_own_employee_gets_409_with_reason_and_pracownik_gets_403` |
| R6 | `Changing_start_moves_first_terms_version_and_cannot_pass_a_later_one` |
| R7, R8 | `Mandate_or_work_…_after_termination` (rozwiązanie i cofnięcie), `EmploymentHttpTests` (status `ended`) |
| R9 | `EmploymentHttpTests.Kadry_create_…` (usunięcie → 404) |
| R10–R12 | `Terms_fields_depend_on_kind`; front: `types.test.ts` |
| R13 | `Minimum_wage_is_checked_with_fte_and_hourly_rate_when_parameter_exists` |
| R14, R15 | `New_terms_close_previous_same_date_corrects_and_earlier_or_outside_is_rejected`, `Last_terms_version_can_be_deleted_but_not_the_only_one` |
| R16, R18 | `Employee_with_contracts_in_retention_cannot_be_deleted_and_status_is_listed`, `EmploymentHttpTests` (409 `retention`) |
| R17 | `Retention_is_counted_from_end_of_year_since_2019_and_50_years_before` |
| R19, R20 | `Reading_terms_is_audited_and_pracownik_has_no_access` |
| R21 | ręcznie: kartoteka „Anna Testowa” (Demo) ma umowę o pracę 6 000,00 zł |

Sprawdzone w przeglądarce (Kadry): zakładka „Umowy”, szuflada nowej umowy ze zmianą rodzaju (pola i ubezpieczenia),
blokada zlecenia w okresie umowy o pracę (komunikat powodu), nowa wersja warunków od 2026-07-01 (poprzednia zamknięta
2026-06-30), usunięcie ostatniej wersji, status na liście kartotek.
