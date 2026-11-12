# ADR-0008: Sesje w bazie, token w ciasteczku serwera frontu, hasła w pgcrypto

- **Status:** Accepted
- **Data:** 2026-11-12

## Kontekst

Logowanie ma działać bez naszej infrastruktury (repo publiczne), a OIDC ma być tylko opcją. Logika jest w bazie
(ADR-0003), a przeglądarka nie rozmawia z API, tylko z server functions frontu (ADR-0005).

## Decyzja

- **Hasła** sprawdza i skrótuje baza: `ext.crypt(hasło, ext.gen_salt('bf', 12))` (`pgcrypto`, bcrypt). Hasło
  przechodzi do bazy w parametrze procedury i nigdzie nie jest zapisywane w postaci jawnej; skrót nie opuszcza bazy.
- **Sesja** to wiersz `platform.sessions` ze skrótem SHA-256 losowego tokenu (32 bajty, generuje API) i czasem
  wygaśnięcia (`Auth__SessionHours`). Wylogowanie i wyłączenie konta usuwają sesje od razu.
- **Token** dostaje serwer frontu i trzyma go w ciasteczku `hr_session` (`httpOnly`, `SameSite=Lax`, `Secure` poza
  Development). Server functions dokładają go jako `Authorization: Bearer` do API. JavaScript w przeglądarce nie
  widzi tokenu.
- **API**: schemat uwierzytelniania `Session` sprawdza token rutyną `platform_app.session_resolve` i ustawia aktora
  w `DbSession`. Grupa `/api` wymaga sesji (`RequireAuthorization`); globalnej polityki nie ma, żeby nieznana trasa
  dawała 404 (np. `/api/auth/dev-login` poza Development).
- **Blokada** po `Auth__MaxFailedAttempts` nieudanych próbach na `Auth__LockoutMinutes`. Porażka logowania nie rzuca
  błędu z procedury (cofnęłoby licznik), tylko zwraca NULL, a API odpowiada 401 z kodem `U0005`, zawsze tym samym.
- **Logowanie deweloperskie** działa tylko dla kont `is_dev_account` (istnieją wyłącznie w kontekście Liquibase
  `dev`), a endpoint jest mapowany tylko w `Development` z `Auth__DevLogin=true`.
- **Pierwszy Administrator** z `Auth__BootstrapAdmin__*` przy starcie, jeśli w bazie nie ma żadnego (poza kontami
  deweloperskimi).

## Konsekwencje

- Każde żądanie API robi jedno zapytanie o sesję; przy dużym ruchu można dodać krótką pamięć podręczną (nowy ADR).
- OIDC (część 2 specyfikacji 001) dołoży drugi sposób zakładania sesji; reszta łańcucha (ciasteczko, Bearer,
  `session_resolve`) zostaje bez zmian.
- Produkcja uruchamia Liquibase z `--contexts=main`, inaczej powstałyby konta deweloperskie.
