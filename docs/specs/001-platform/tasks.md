# 001 Platforma — zadania

[Specyfikacja](spec.md) · [Plan](plan.md)

- [x] T1. Baza: `pgcrypto`, `btree_gist`, tabele `users`, `user_roles`, `sessions`, `audit_entries`,
      `read_audit_entries`; funkcje `require_role`, `audit`, `read_audit`; rutyny sesji i `me`, `bootstrap_admin`;
      konta deweloperskie (`context: dev`). Testy rutyn sesji (R1–R5, R8, R10–R12).
- [x] T2. API: `AuthOptions`, handler `Session`, `DbSession.ActorId`, endpointy `/api/auth/*`, bootstrap przy starcie,
      `RequireAuthorization` na grupach, kod `U0005`. Testy HTTP (R6–R9 w tym 404 w Production, R10).
- [x] T3. Stanowiska wymagają ról (R13) — rutyny `require_role` + audyt (R22); testy zaktualizowane o sesję.
- [x] T4. Front: sesja w ciasteczku, server functions z `Authorization`, `/login` z przyciskami deweloperskimi,
      guard w root, menu według ról, wylogowanie.
- [x] T5. Spółki: tabela, `nip_valid`, `regon_valid`, rutyny, endpointy, ekran (R16–R17). Testy.
- [x] T6. Parametry prawne: tabela z `EXCLUDE`, dane 2026, `legal_parameter`, rutyny, endpointy, ekran (R18–R21). Testy.
- [x] T7. Użytkownicy: rutyny, endpointy, ekran (R14–R15). Testy.
- [x] T8. Audyt: `audit_list`, endpoint, ekran (R24).
- [x] T9. ADR-0008, `db/README.md` (kod `U0005`), README, migawka OpenAPI.

## Mapa wymaganie → test

| R | Test |
|---|---|
| R1, R4, R5 | `SessionRoutineTests` (login/e-mail bez wielkości liter, złe hasło, blokada i odblokowanie, konto wyłączone) |
| R2 | `SessionRoutineTests.Expired_and_logged_out_sessions_do_not_resolve`, `AuthHttpTests.Login_me_logout` |
| R3 | ręcznie w przeglądarce: ciasteczko `hr_session` `httpOnly`; kod: `features/auth/auth.functions.ts` |
| R6 | `AuthHttpTests.Api_requires_session` |
| R7 | `AuthHttpTests.Login_me_logout` |
| R8, R9 | `AuthHttpTests.Dev_login_is_enabled_in_development`, `Dev_login_does_not_exist_outside_development`, `SessionRoutineTests.Dev_login_uses_only_dev_accounts` |
| R10 | `AuthHttpTests.Bootstrap_admin_is_created_once` |
| R11, R13 | `PositionRoutineTests.Create_and_list_require_kadry_or_administrator`, `AuthHttpTests.Pracownik_gets_403_on_positions` |
| R12, R22 | `PositionRoutineTests.Create_returns_id_trims_name_and_writes_audit`, `CompanyRoutineTests`, `LegalParameterRoutineTests` |
| R14, R15 | `UserHttpTests` |
| R16, R17 | `CompanyRoutineTests`, `CompanyHttpTests` |
| R18–R21 | `LegalParameterRoutineTests`, `LegalParameterHttpTests` |
| R23 | `EmployeeRoutineTests.Reading_details_is_audited_for_kadry_but_not_for_the_employee` (moduł 003) |
| R24 | `AuditHttpTests` |
