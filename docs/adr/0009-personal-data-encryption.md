# ADR-0009: Szyfrowanie PESEL i rachunku w bazie, klucz z env w ustawieniu sesji

- **Status:** Accepted
- **Data:** 2026-11-12

## Kontekst

Decyzja 10 właściciela: PESEL i numer rachunku mają być w bazie zaszyfrowane, klucz pochodzi tylko z env, a
wyszukiwanie i unikalność PESEL w spółce działają po skrócie z kluczem (HMAC). Logika jest w bazie (ADR-0003):
walidacja PESEL (suma kontrolna, zgodność z datą urodzenia), unikalność w spółce i audyt odczytu robią rutyny.

Rozważone warianty:

1. **Szyfrowanie w C#** (AES-GCM, HMAC w .NET). Baza dostaje tylko szyfrogram i skrót. Klucz nie opuszcza procesu
   API. Ale reguły PESEL musiałyby przejść do C# (baza nie widzi wartości), a odczyt przez inne kanały (raporty,
   PIT z bazy) też musiałby iść przez API. To rozbija zasadę „logika w bazie”.
2. **Szyfrowanie w bazie** (`pgcrypto`: `pgp_sym_encrypt`, `hmac`). Rutyny dostają jawną wartość, walidują ją,
   szyfrują i liczą HMAC. Klucz musi dotrzeć do bazy.

## Decyzja

Wariant 2.

- Klucz: `Data__EncryptionKey` (co najmniej 32 znaki), walidowany przy starcie API. Nie ma wartości domyślnej poza
  plikiem `.env` lokalnego compose i konfiguracją testów.
- API ustawia klucz w ustawieniu sesji `hr.data_key` przy każdym otwarciu połączenia (jak aktora, `hr.actor_id`).
  Wartość idzie parametrem (`set_config($1, $2, false)`), więc nie ma jej w tekście zapytania. Rutyny czytają klucz
  przez `platform.data_key()`; pusty klucz → błąd, nigdy szyfrowanie pustym kluczem.
- Kolumny: `*_enc bytea` (`pgp_sym_encrypt`, AES-128 z losową solą), `pesel_hmac bytea` (`hmac(pesel, klucz,
  'sha256')`) z unikalnością `(company_id, pesel_hmac)`. Dla rachunku przechowujemy jawnie 4 ostatnie cyfry do
  wyświetlenia na liście.
- Odszyfrowanie tylko w rutynie szczegółów kartoteki, która zapisuje audyt odczytu.

## Konsekwencje

- Klucz jest w pamięci sesji serwera bazy. Inna sesja go nie odczyta (ustawienia sesji są prywatne), ale
  `log_statement = all` z logowaniem parametrów albo `auto_explain` z parametrami pokazałyby go w logu — na produkcji
  takie logowanie musi być wyłączone (zapisane w `db/README.md`).
- Kopia bazy bez klucza nie ujawnia PESEL ani rachunku. Utrata klucza = utrata tych danych; klucz trzeba trzymać
  w sejfie sekretów razem z kopią zapasową.
- Zmiana klucza wymaga przeszyfrowania danych (procedura z dwoma kluczami) — osobny ADR, gdy będzie potrzebna.
- Dane deweloperskie w Liquibase nie mogą zawierać PESEL (Liquibase nie zna klucza).
