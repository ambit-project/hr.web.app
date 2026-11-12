# 002 Słowniki — zadania

[Specyfikacja](spec.md) · [Plan](plan.md)

- [x] T1. Źródła: oficjalny wykaz kodów ZUS i schemat kodów urzędów skarbowych; changesety danych ze źródłem (R4, R8).
- [x] T2. Baza: tabele `zus_codes`, `tax_offices`, kolumna `companies.tax_office_id`; rutyny słowników
      i `zus_code_valid`; zmiana rutyn spółek i `position_list`, `position_update`. Testy rutyn.
- [x] T3. API: endpointy stanowisk, kodów ZUS, urzędów, pole urzędu w spółkach. Testy HTTP, migawka OpenAPI.
- [x] T4. Front: menu „Słowniki”, edycja stanowiska, ekrany kodów ZUS i urzędów, urząd w spółce.
- [x] T5. Dokumentacja: model (atrybut `insurances` → 004), `PRODUCT.md`, `specs/README.md`, mapa wymaganie → test.

## Mapa wymaganie → test

| R | Test |
|---|---|
| R1 | `PositionRoutineTests.Update_deactivates_checks_duplicates_and_row_version`, `DictionaryHttpTests.Position_edit_with_stale_row_version_is_409` |
| R2 | `PositionRoutineTests.Update_deactivates_checks_duplicates_and_row_version` (lista bez nieaktywnych); front: przełącznik w URL |
| R3, R6 | `ZusCodeRoutineTests` (format kodu dla rodzaju, duplikat, `beforeFrom`, wersja wiersza), `DictionaryHttpTests.Administrator_validation_errors_come_back_per_field` |
| R4 | `ZusCodeRoutineTests.Official_codes_are_loaded_by_migration` |
| R5 | `ZusCodeRoutineTests.Kadry_read_only_administrator_writes`, `DictionaryHttpTests.Kadry_read_zus_codes_and_tax_offices_but_cannot_write_them` |
| R7 | `ZusCodeRoutineTests.Create_writes_audit_and_code_is_valid_only_inside_its_period` |
| R8 | `TaxOfficeRoutineTests.Official_list_is_loaded_by_migration` |
| R9 | `TaxOfficeRoutineTests` (wyszukiwanie, kod, duplikat, 403, wyłączenie) |
| R10 | `CompanyRoutineTests.Tax_office_is_required_and_must_be_active`, `Company_keeps_its_tax_office_after_it_becomes_inactive`, `CompanyHttpTests` |
| R11 | testy audytu w `ZusCodeRoutineTests`, `TaxOfficeRoutineTests`; `U0004` w testach ról |
| R12 | ręcznie w przeglądarce: menu „Słowniki”, przyciski zapisu tylko dla Administratora |
