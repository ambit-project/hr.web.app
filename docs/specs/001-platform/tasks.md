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
- [ ] T7. Użytkownicy: rutyny, endpointy, ekran (R14–R15). Testy.
- [ ] T8. Audyt: `audit_list`, endpoint, ekran (R24).
- [ ] T9. ADR-0008, `db/README.md` (kod `U0005`), README, migawka OpenAPI.
