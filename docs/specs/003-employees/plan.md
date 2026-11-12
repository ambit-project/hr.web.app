# 003 Pracownicy — plan

[Specyfikacja](spec.md)

## Baza (`db/changelog`)

Schematy `employees` (tabele) i `employees_app` (kontrakt).

| Tabela | Kolumny (skrót) |
|---|---|
| `employees.employees` | id, company_id (FK), number (UK w spółce), first_name, second_name, last_name, pesel_enc bytea, pesel_hmac (UK w spółce), identity_document_enc bytea, birth_date, tax_office_id (FK), email, created_at, created_by |
| `employees.addresses` | id, employee_id, kind (`residence`, `correspondence`), street, postal_code, city, country, valid_from, valid_to — `EXCLUDE` nakładania okresów jednego rodzaju |
| `employees.bank_accounts` | id, employee_id, iban_enc bytea, iban_last4, valid_from, valid_to — `EXCLUDE` nakładania |
| `platform.user_employee_links` | id, user_id, employee_id (UK), company_id (UK z user_id), linked_at, linked_by |

Szyfrowanie (ADR-0009): `ext.pgp_sym_encrypt(wartość, klucz)` i `ext.hmac(pesel, klucz, 'sha256')`. Klucz czyta
`platform.data_key()` z ustawienia sesji `hr.data_key` (ustawia API przy otwarciu połączenia); brak → błąd.

Rutyny `employees_app` (Kadry, Administrator):

| Rutyna | R |
|---|---|
| `employee_list(company_id, search, limit, offset)` | R5 |
| `employee_get(id)` → dane z odszyfrowanym PESEL, dokumentem, bieżącym rachunkiem; zapisuje `read_audit` | R9 |
| `employee_create(company_id, number, first_name, second_name, last_name, pesel, identity_document, birth_date, tax_office_id, email, street, postal_code, city, address_from)` | R1–R3 |
| `employee_update(id, row_version, …)` | R4 |
| `employee_delete(id)` | R11 |
| `address_list(employee_id)`, `address_add(employee_id, kind, street, postal_code, city, valid_from)` | R6 |
| `bank_account_list(employee_id)` (zamaskowane), `bank_account_add(employee_id, iban, valid_from)`, `bank_account_close(employee_id, valid_to)` | R7 |
| `link_account(employee_id, user_login_or_email)`, `unlink_account(employee_id)` | R12 |
| `read_audit_list(employee_id, limit, offset)` (Administrator) | R10 |

Wewnętrzne: `employees.pesel_valid(text)`, `employees.pesel_birth_date(text)`, `employees.iban_valid(text)`,
`platform.data_key()`. `platform.me_profiles()` → `platform_app.me` dostaje kolumnę `profiles jsonb` (R13).
`platform.read_audit` pomija odczyt pracownika powiązanego z kartoteką.

Dane deweloperskie (`context: dev`): spółka „Demo”, kartoteka „Anna Testowa” bez PESEL, powiązana z `pracownik`.

## API

- `Data:EncryptionKey` (`DataOptions`, walidacja przy starcie: co najmniej 32 znaki). `DbSession` ustawia
  `hr.data_key` przy otwarciu połączenia, razem z aktorem.
- `/api/employees` (GET lista, POST), `/api/employees/{id}` (GET, PUT, DELETE),
  `/api/employees/{id}/addresses` (GET, POST), `/api/employees/{id}/bank-accounts` (GET, POST),
  `/api/employees/{id}/bank-accounts/close` (POST), `/api/employees/{id}/account` (PUT, DELETE),
  `/api/employees/{id}/read-audit` (GET).
- `MeResponse.Profiles` z bazy.

## Front

- Menu „Kadry”: Pracownicy (Kadry, Administrator).
- `/employees`: lista z filtrem spółki (`Select`), wyszukiwaniem i stronicowaniem w URL; szuflada nowej kartoteki.
- `/employees/$id`: strona szczegółów z zakładkami w URL: Dane (edycja w szufladzie, usunięcie z potwierdzeniem),
  Adresy, Rachunek, Konto, Odczyty (Administrator).
- `/my`: lista profili (spółki) z `me`.

## Testy

- Baza: PESEL (suma, data, duplikat w spółce, ten sam w innej spółce), numer automatyczny, szyfrowanie (w tabeli brak
  jawnego PESEL), wyszukiwanie po PESEL, audyt zmian bez wartości PESEL, `read_audit` dla Kadr i brak wpisu dla
  pracownika, adresy i rachunki z zamykaniem okresu, IBAN, powiązanie konta (role, unikalność), usunięcie, role.
- HTTP: start bez klucza → błąd walidacji opcji, `me` z profilami, Pracownik 403 na liście.
- Front: `yarn check`, `yarn build`, przejście w przeglądarce.

## ADR

- ADR-0009: szyfrowanie PESEL i rachunku w bazie (`pgcrypto`) z kluczem z env przekazywanym w ustawieniu sesji.
