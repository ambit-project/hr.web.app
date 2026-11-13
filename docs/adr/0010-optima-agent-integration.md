# ADR-0010: Integracja z Optimą przez agenta, który odpytuje API co 500 ms

- **Status:** Accepted
- **Data:** 2026-11-13

## Kontekst

Comarch ERP Optima działa u klienta na jego SQL Serverze, zwykle bez adresu dostępnego z internetu. hr.web.app ma
z niej pobierać rodzaje płac i listy płac oraz wysyłać do niej pracowników, potrącenia i dodatki. Po stronie klienta
działa już otwarty Ambit Optima Integrator (usługa Windows z wąskimi prawami w SQL Serverze), który pyta aplikację HR
o pracę i opisuje swój kontrakt HTTP.

Rozważone warianty:

1. **hr.web.app łączy się z SQL Serverem klienta** — wymaga otwarcia portu i trzymania loginu SQL w aplikacji.
2. **Agent u klienta odpytuje hr.web.app** (kolejka operacji w bazie hr.web.app) — u klienta nic nie nasłuchuje.
3. **Kanał stały (WebSocket / SignalR)** — szybszy, ale trudniejszy w utrzymaniu przez proxy i restarty usługi.

## Decyzja

Wariant 2, zgodny z kontraktem integratora.

- **Kolejka w bazie** (`optima.operations`): operator zleca, agent bierze (`claim` z `FOR UPDATE SKIP LOCKED`),
  wykonuje raz i odsyła wynik. Powtórkę zleca tylko operator (ten sam id, `attempt + 1`); wynik starszej albo
  powtórzonej próby jest ignorowany.
- **Odpytywanie co 500 ms** (domyślne w integratorze, `AmbitApi:PollIntervalMilliseconds`). Pusty `claim` jest
  jednym odczytem indeksu częściowego `WHERE state = 'pending'`, bez audytu, a „ostatnio widziany” klucza zapisuje się
  najwyżej raz na minutę. Po błędzie połączenia agent czeka `RetrySeconds`.
- **Osobny schemat uwierzytelnienia** `OptimaAgent`: token w `X-Optima-Agent-Key`, w bazie tylko SHA-256 (token ma
  256 bitów losowości, więc bez soli i bez wolnego skrótu). Endpointy agenta są poza grupą `/api` z sesją i mają
  własną politykę — klucz i sesja się nie mieszają. Rutyny agenta biorą skrót tokenu i same sprawdzają, że klucz
  trzyma operację (zasada ADR-0003: uprawnienia w bazie).
- **Spółka ↔ baza Optimy 1:1** z kontrolą NIP; nowa baza z NIP wolnej spółki wiąże się sama.
- **Dane osobowe** (PESEL, dokument) odszyfrowane tylko w odpowiedzi dla agenta, z audytem odczytu w imieniu operatora
  (agent nie ma konta).
- **Listy płac z Optimy** są kopią tylko do odczytu w schemacie `optima`, niezależną od list płac hr.web.app (009).

## Konsekwencje

- Opóźnienie od zlecenia do startu operacji: do ~0,5 s. Każdy agent to ~2 lekkie żądania na sekundę — przy wielu
  instalacjach do obserwacji (pula połączeń, log żądań).
- Kontrakt jest współdzielony z repo integratora: zmiana ścieżki albo pola wymaga zmiany po obu stronach
  (`docs/hr-application-api.md` integratora, specyfikacja 011, migawka OpenAPI).
- Odwołanie klucza działa od następnego żądania; operacja wydana odwołanemu kluczowi zostaje `claimed` do ponownej
  próby.
