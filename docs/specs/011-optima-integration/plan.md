# 011 Integracja z Optimą — plan

[Specyfikacja](spec.md) · [ADR-0010](../../adr/0010-optima-agent-integration.md)

## Decyzje (właściciel: „sam zdecyduj”)

- **Numer 011**, bo 007–010 są zarezerwowane w `PRODUCT.md`; moduł bazy `95-optima` zostawia miejsce na 007–010.
- **Wszystkie rodzaje płac** (także nieaktywne i wycofane) — z flagami; eksport używa tylko aktywnych.
- **Powiązanie ręczne** rodzaju płacy ze składnikiem — import nie zakłada składników, bo 006 wymaga cech prawnych,
  których Optima nie przekazuje w tej operacji.
- **Dodatki osobną operacją** `ExportAllowances`, lustrzaną do `ExportDeductions` (zgodność z istniejącym agentem).
- **Składniki procentowe i godzinowe pomijane** z kodem `notFixed` — kwotę liczy lista płac (009), nie integracja.
- **Listy płac: pełne** (nagłówki, wypłaty, elementy) za jeden miesiąc deklaracji na operację — limit 20 MB.
- **Jeden rodzaj klucza na instalację**, kluczy może być kilka (wymiana bez przerwy); zakłada Administrator.
- **Tylko API** — ekrany w osobnym zadaniu.

## Baza

Schematy `optima` (tabele, funkcje wewnętrzne) i `optima_app` (kontrakt). Changeset `20261113-25`.

| Tabela | Kolumny (skrót) |
|---|---|
| `agent_keys` | id, name, key_hash (UK, 32 bajty), key_prefix, created_*, revoked_*, last_seen_at, last_agent_version |
| `databases` | database_name (PK), tax_number, last_seen_at |
| `company_links` | company_id (PK, FK kaskada), database_name (UK) |
| `operations` | id, kind, company_id, database_name, year, month, employee_ids, skipped, state, attempt, claimed_*, finished_at, succeeded, error_code, log — indeks częściowy `WHERE state = 'pending'` |
| `pay_types` | (database_name, optima_id) PK, cechy z Optimy, missing_since, component_type_id (UK w bazie) |
| `payroll_lists` → `payouts` → `payout_elements` | kopia list płac z Optimy, UK (database_name, optima_id), kaskada |

Rutyny `optima_app`: `agent_key_create/list/revoke/resolve`, `database_list`, `company_link_set`,
`pay_type_list/map`, `operation_create/list/get/retry`, `payroll_get`; agent: `agent_claim`, `agent_operation`,
`agent_employees`, `agent_components`, `agent_result`. Funkcje wewnętrzne: `agent_key_id`, `held_operation`,
`read_audit_as`, `try_uuid`, `database_seen`, `linked_database`, `export_items`, `current_employment`,
`store_pay_types`, `store_payrolls`. Zasoby audytu odczytów: `optima_export`, `optima_payroll`.

Rutyny agenta biorą **skrót tokenu**, nie id klucza: baza sama sprawdza, że klucz jest aktywny i trzyma operację.

## API

Operator (sesja, grupa `/api`, tag `Optima`):

| Metoda | Ścieżka | Rola |
|---|---|---|
| GET / POST | `/api/optima/agent-keys` | Administrator |
| DELETE | `/api/optima/agent-keys/{id}` | Administrator |
| GET | `/api/optima/databases` | Kadry, Administrator |
| PUT | `/api/optima/companies/{companyId}/database` | Administrator |
| GET | `/api/optima/companies/{companyId}/pay-types` | Kadry, Administrator |
| PUT | `/api/optima/companies/{companyId}/pay-types/{optimaId}` | Kadry, Administrator |
| GET | `/api/optima/companies/{companyId}/payrolls?year&month` | Kadry, Administrator |
| POST / GET | `/api/optima/operations` | Kadry, Administrator |
| GET | `/api/optima/operations/{id}` | Kadry, Administrator |
| POST | `/api/optima/operations/{id}/retry` | Kadry, Administrator |

Agent (klucz, osobna grupa poza `/api` z sesją, polityka `OptimaAgent`, tag `OptimaAgent`): `GET connection`,
`POST operations/claim`, `GET operations/{id}/employees|deductions|allowances|payroll-period`,
`POST operations/{id}/result` — ścieżki z kontraktu integratora.
