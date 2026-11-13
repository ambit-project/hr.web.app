# 011 Integracja z Optimą — zadania

[Specyfikacja](spec.md) · [Plan](plan.md)

- [x] T1. Baza: schematy `optima`/`optima_app`, tabele, rutyny operatora i agenta, uprawnienia, zasoby audytu
      odczytów. Testy rutyn.
- [x] T2. API: schemat uwierzytelnienia `OptimaAgent`, endpointy agenta i operatora, migawka OpenAPI. Testy HTTP.
- [x] T3. Dokumentacja: ADR-0010, kody błędów w `db/README.md`, PRODUCT.
- [ ] T4. Front: klucze agenta, powiązanie spółek, rodzaje płac, operacje z logiem, podgląd list płac (osobne zadanie).
- [ ] T5. Test end-to-end z działającym integratorem na lokalnej Optimie (zapis tylko w firmie testowej).

## Mapa wymaganie → test

Testy rutyn: `tests/HrWebApp.Api.Tests/Database/OptimaRoutineTests.cs`; HTTP:
`tests/HrWebApp.Api.Tests/Features/Optima/OptimaHttpTests.cs`.

| R | Test |
|---|---|
| R1, R2, R3 | `Revoked_key_is_refused_and_key_and_session_do_not_mix`, `Agent_endpoints_refuse_missing_malformed_or_unknown_key`, `Only_administrator_manages_agent_keys` |
| R4, R5 | `Two_parallel_claims_never_get_the_same_operation`, `Claim_offers_only_supported_kinds` |
| R6 | `Empty_claim_writes_no_audit_and_last_seen_at_most_once_a_minute` |
| R7 | `Add_employees_hands_out_decrypted_data_and_audits_the_read` (404 przed wydaniem i dla innego rodzaju) |
| R8 | `Read_payroll_components_end_to_end_links_company_by_tax_number` (powtórzony wynik), `Read_payrolls_end_to_end_and_retry_ignores_the_older_attempt`, `Result_for_an_operation_held_by_another_key_is_not_found`, `Result_with_a_malformed_payload_is_a_bad_request` |
| R9 | `Read_payrolls_end_to_end_and_retry_ignores_the_older_attempt` |
| R10 | `Read_payroll_components_end_to_end_links_company_by_tax_number` |
| R11, R12 | `Operation_for_unlinked_company_is_refused`, `Operation_for_a_company_whose_database_reports_another_tax_number_is_a_conflict` |
| R13, R15 | `Read_payroll_components_end_to_end_links_company_by_tax_number`, `Pay_type_missing_from_a_later_read_is_marked_and_keeps_its_mapping` |
| R14 | `Mapping_a_deduction_pay_type_to_an_allowance_is_refused` |
| R16, R17 | `Add_employees_hands_out_decrypted_data_and_audits_the_read` |
| R18, R19 | `Export_allowances_sends_mapped_fixed_items_and_records_skipped` |
| R20, R21, R22 | `Read_payrolls_end_to_end_and_retry_ignores_the_older_attempt` |
