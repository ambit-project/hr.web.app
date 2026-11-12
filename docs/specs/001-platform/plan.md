# 001 Platforma — plan

[Specyfikacja](spec.md)

## Baza (`db/changelog`)

Rozszerzenie `pgcrypto` (zaufane, zakłada je właściciel bazy) do skrótów haseł (`crypt` + `gen_salt('bf', 12)`)
i tokenów (`digest(…, 'sha256')`).

| Tabela (`platform`) | Kolumny (skrót) |
|---|---|
| `users` | id, login (UK lower), email (UK lower), display_name, password_hash, is_active, failed_attempts, locked_until, created_at |
| `user_roles` | user_id, role (`kadry`, `administrator`, `pracownik`) — PK (user_id, role) |
| `sessions` | id, user_id, token_hash (UK), created_at, expires_at, last_seen_at |
| `companies` | id, short_name (UK lower), name, nip (UK), regon, street, postal_code, city, country, is_active, created_at, created_by |
| `legal_parameters` | id, code, value numeric, valid_from, valid_to, source — bez nakładania (`EXCLUDE` na `daterange`, `btree_gist`) |
| `audit_entries` | id, at, actor_id, entity, entity_id, action, before jsonb, after jsonb |
| `read_audit_entries` | id, at, actor_id, employee_id, resource, resource_id |

Rutyny `platform_app` (rola API: `EXECUTE`), wszystkie `SECURITY DEFINER`:

| Rutyna | Kto | R |
|---|---|---|
| `session_login(login, password, token, hours, max_failed, lockout_minutes)` → user_id | bez sesji | R1–R5 |
| `session_dev_login(role, token, hours)` → user_id | bez sesji (endpoint tylko w Development) | R8–R9 |
| `session_resolve(token)` → user_id, roles | bez sesji | R2, R5, R6 |
| `session_logout(token)` | bez sesji | R2 |
| `me()` → konto i role | zalogowany | R7 |
| `bootstrap_admin(login, email, display_name, password)` → bool | start API | R10 |
| `user_list`, `user_create`, `user_update` | Administrator | R14–R15 |
| `company_list` (z `row_version`), `company_create`, `company_update` | lista/get: Kadry, Administrator; zapis: Administrator | R16–R17 |
| `legal_parameter_list(code, on_date)`, `legal_parameter_add` | Administrator | R18–R19 |
| `audit_list(entity, entity_id, actor_id, from, to, limit, offset)` | Administrator | R24 |

Wewnętrzne (`platform`): `actor_id()`, `require_role(VARIADIC text[])` → `U0004`, `audit(entity, id, action, before,
after)`, `read_audit(employee_id, resource, resource_id)`, `legal_parameter(code, date)` (R21), `nip_valid(text)`,
`regon_valid(text)`.

Dane: parametry 2026 (changeset `data`, R20); konta deweloperskie `admin`, `kadry`, `pracownik` w changesecie
z `context: dev` (compose i testy uruchamiają Liquibase z `--contexts=dev`; produkcja bez).

## API

- `DbSession` ustawia `hr.actor_id` po otwarciu połączenia z `ActorId` (R12).
- Uwierzytelnianie: własny `AuthenticationHandler` schematu `Session` czyta `Authorization: Bearer <token>`, woła
  `session_resolve`, ustawia `ClaimsPrincipal` (id, role) i `DbSession.ActorId`. Wszystkie grupy `/api/*` poza
  `/api/auth/login`, `/api/auth/dev-login`, `/api/auth/options` wymagają uwierzytelnienia (`RequireAuthorization`).
  Role sprawdza baza; API nie powtarza reguł ról.
- Token: 32 bajty z `RandomNumberGenerator`, base64url; zwracany tylko w odpowiedzi logowania.
- Opcje (`AuthOptions`, sekcja `Auth`, walidacja przy starcie): `SessionHours`, `MaxFailedAttempts`,
  `LockoutMinutes`, `MinPasswordLength`, `DevLogin`, `BootstrapAdmin` (Login, Email, DisplayName, Password).
- Endpointy: `POST /api/auth/login`, `POST /api/auth/dev-login` (mapowany tylko w Development z `DevLogin`),
  `POST /api/auth/logout`, `GET /api/auth/me`, `GET /api/auth/options` (czy jest logowanie deweloperskie);
  `/api/users`, `/api/companies`, `/api/legal-parameters`, `/api/audit`.
- Błędy logowania: zawsze 401 z kodem `U0005` (nieprawidłowe dane albo blokada) — nowy kod w katalogu.

## Front

- Ciasteczko `hr_session` ustawiają server functions logowania (`setCookie`), czytają wszystkie server functions
  (`getCookie`) i dokładają `Authorization`. 401 z API → server function zwraca `unauthorized`, a root route
  przekierowuje na `/login?redirect=…`.
- Root `beforeLoad` pobiera `me` (server function); brak sesji i trasa inna niż `/login` → przekierowanie.
- `/login`: formularz login/hasło; przyciski deweloperskie, gdy `options.devLogin`.
- Menu według ról: Stanowiska (Kadry, Administrator); Administracja (Administrator): Użytkownicy, Spółki,
  Parametry prawne, Audyt. Pasek z nazwą konta i „Wyloguj”.

## Testy

- Baza: logowanie dobre i złe hasło, blokada po N próbach i odblokowanie po czasie, nieaktywne konto, wygaśnięcie,
  wylogowanie, `require_role` dla każdej rutyny zapisu, NIP i REGON (poprawne i błędne sumy), parametry bez
  nakładania i automatyczne zamknięcie poprzedniego okresu, audyt dla każdego zapisu.
- HTTP: 401 bez sesji, 403 bez roli, logowanie → `me`, dev-login w Development działa, a z `Environment=Production`
  zwraca 404; bootstrap administratora.
- Front: `yarn check`, `typecheck`, Vitest dla obsługi 401; przejście w przeglądarce: logowanie każdą rolą,
  menu według ról.

## ADR

- ADR-0008: sesje w bazie z tokenem w ciasteczku `httpOnly` serwera frontu, hasła `pgcrypto` (bcrypt) w bazie.
