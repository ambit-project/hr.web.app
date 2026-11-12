# 003 Pracownicy — zadania

[Specyfikacja](spec.md) · [Plan](plan.md)

- [x] T1. ADR-0009; `platform.data_key()`, `DataOptions`, klucz w `DbSession`, compose i testy z kluczem.
- [x] T2. Baza: tabele, `pesel_valid`, `iban_valid`, rutyny kartoteki, adresów, rachunków, powiązania, `me` z profilami,
      `read_audit` bez wpisu dla pracownika, dane deweloperskie. Testy rutyn.
- [x] T3. API: endpointy, profile w `me`. Testy HTTP, migawka OpenAPI.
- [x] T4. Front: lista, nowa kartoteka, strona szczegółów z zakładkami, profile w `/my`.
- [x] T5. Dokumentacja: kody błędów, README (zmienna `Data__EncryptionKey`), PRODUCT, mapa wymaganie → test.

## Mapa wymaganie → test

| R | Test |
|---|---|
| R1, R2 | `EmployeeRoutineTests.Empty_number_gets_next_number_in_company`, `EmployeeHttpTests.Kadry_create_read_edit_and_delete_an_employee` |
| R3 | `EmployeeRoutineTests.Pesel_checksum_and_date_are_checked`, `Pesel_from_2000s_gives_birth_date_with_month_plus_20`, `Pesel_is_unique_in_company_but_not_across_companies`, `Birth_date_must_match_pesel_and_pesel_or_document_is_required`; front: `pesel.test.ts` |
| R4 | `EmployeeHttpTests.Kadry_create_read_edit_and_delete_an_employee` (409), `EmployeeRoutineTests.Audit_of_change_has_no_pesel_value` |
| R5 | `EmployeeRoutineTests.Pesel_is_stored_encrypted_and_found_by_exact_search`, `EmployeeHttpTests` (wyszukiwanie po nazwisku) |
| R6 | `EmployeeRoutineTests.New_address_closes_previous_and_earlier_date_is_rejected` |
| R7 | `EmployeeRoutineTests.Iban_is_normalized_and_checked`, `EmployeeHttpTests` (rachunek w szczegółach) |
| R8 | `EmployeeRoutineTests.Pesel_is_stored_encrypted_and_found_by_exact_search`, `EmployeeHttpTests.Api_does_not_start_without_encryption_key` |
| R9 | `EmployeeRoutineTests.Reading_details_is_audited_for_kadry_but_not_for_the_employee` |
| R10 | ręcznie w przeglądarce: zakładka „Odczyty” (Administrator) |
| R11 | `EmployeeHttpTests.Kadry_create_read_edit_and_delete_an_employee` (usunięcie → 404) |
| R12, R13 | `EmployeeHttpTests.Linked_account_sees_profiles_and_pracownik_gets_403_on_employees` |
| R14 | `EmployeeRoutineTests.Pracownik_cannot_list_or_read_employees`, `EmployeeHttpTests` (403) |
| R15 | ręcznie: logowanie deweloperskie Pracownika pokazuje profil „Demo” |
