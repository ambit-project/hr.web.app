# ADR-0003: Logika w bazie: procedury, funkcje, role

- **Status:** Accepted
- **Data:** 2026-11-12

## Kontekst

Reguły kadrowo-płacowe (walidacja, stany list płac, przeliczenia, uprawnienia do danych) mają jedno źródło prawdy
i mają być sprawdzalne testami na danych. Właściciel zdecydował: logika w PostgreSQL, bez EF Core.

## Decyzja

- **Zapis tylko procedurą (`CALL`), odczyt tylko funkcją `RETURNS TABLE`** ze schematu `<moduł>_app`. Tabele leżą
  w schemacie `<moduł>`.
- **Role:** `hr_owner` (`NOLOGIN`) jest właścicielem wszystkiego; `hr_migrator` loguje się dla Liquibase i w bazie
  działa jako `hr_owner` (`ALTER ROLE … IN DATABASE … SET role`); `hr_api` ma tylko `EXECUTE` na rutynach
  `<moduł>_app`. Błąd `42501` naprawia się rutyną, nie grantem do tabeli.
- **Rutyny kontraktu:** `SECURITY DEFINER`, `SET search_path = pg_catalog, pg_temp`.
- **Błędy:** `SQLSTATE U0001–U0004` (katalog w `db/README.md`); walidacja zwraca wszystkie pola naraz w `HINT`
  jako JSON z kodami. API tłumaczy je na ProblemDetails (`DbErrorHandler`).
- **C#:** czysty Npgsql (`NpgsqlCommand`, parametry pozycyjne), bez Dappera; handler bez biblioteki (klasa
  w DI) woła jedną rutynę; jedno połączenie na zakres żądania (`DbSession`).
- **Aktor:** ustawienie sesji `hr.actor_id`, czytane przez `platform.actor_id()`. Podpięcie do logowania — w
  module uwierzytelniania.

## Konsekwencje

Walidacja kształtu w C# (atrybuty `MaxLength`) służy tylko opisowi w OpenAPI; regułę sprawdza procedura.
