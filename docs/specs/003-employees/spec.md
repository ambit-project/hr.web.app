# 003 Pracownicy — specyfikacja

Model: [03-employees.md](../../domain/03-employees.md), powiązanie z kontem:
[01-platform.md → UserEmployeeLink](../../domain/01-platform.md#useremployeelink). Decyzje właściciela: osobna
kartoteka w każdej spółce (decyzja 2), Pracownik widzi tylko swoje profile (decyzja 9), szyfrowanie, audyt zmian
i odczytów, retencja (decyzja 10).

## Cel

Kadry prowadzą kartotekę osoby w spółce: dane osobowe i podatkowe, adres zamieszkania z historią i rachunek do
wypłaty. PESEL i numer rachunku są w bazie zaszyfrowane, a każde wejście w kartotekę zostawia ślad. Kadry wiążą
kartotekę z kontem, a Pracownik po zalogowaniu widzi swoje profile, po jednym na spółkę.

## Wymagania

### Kartoteka
- **R1.** Kadry i Administrator zakładają kartotekę w wybranej spółce: imię, drugie imię, nazwisko, PESEL albo
  dokument tożsamości (gdy osoba nie ma PESEL), data urodzenia, urząd skarbowy (aktywny), e-mail, numer ewidencyjny
  i adres zamieszkania od daty. Spółki kartoteki nie da się zmienić.
- **R2.** Numer ewidencyjny jest unikalny w spółce. Pusty numer = kolejny wolny numer liczbowy w spółce.
- **R3.** PESEL: 11 cyfr z poprawną sumą kontrolną; data urodzenia zapisana w PESEL musi być równa podanej
  (`mismatch`). PESEL jest unikalny w spółce (`duplicate`); ta sama osoba w innej spółce to osobna kartoteka.
  Wymagany jest PESEL albo dokument tożsamości (`required` na `pesel`).
- **R4.** Edycja danych kartoteki z wersją wiersza (nieaktualna → `U0003`). Każda zmiana w audycie zmian, ale PESEL
  i rachunek w audycie występują tylko jako „zmieniono”, bez wartości.
- **R5.** Lista kartotek: filtr spółki, wyszukiwanie po nazwisku, imieniu i numerze, a po pełnym PESEL — dokładne
  dopasowanie. Lista nie pokazuje PESEL ani rachunku.

### Adresy i rachunek
- **R6.** Adres (zamieszkania albo korespondencyjny) dodaje się od daty; poprzedni otwarty adres tego rodzaju kończy
  się dzień wcześniej. Data wcześniejsza niż początek obecnego → `beforeCurrent`; ta sama data poprawia adres.
- **R7.** Rachunek do wypłaty (polski IBAN: `PL` + 26 cyfr, suma kontrolna mod 97) dodaje się od daty tak samo jak
  adres. Rachunek można zamknąć datą (wypłata gotówką od następnego dnia).

### Ochrona danych
- **R8.** PESEL, dokument tożsamości i numer rachunku są w tabelach zaszyfrowane kluczem z konfiguracji
  (`Data__EncryptionKey`), nigdy z repo ani z UI. Unikalność i wyszukiwanie PESEL działają po skrócie HMAC tym samym
  kluczem. Bez klucza API nie startuje. Wybór miejsca szyfrowania: [ADR-0009](../../adr/0009-personal-data-encryption.md).
- **R9.** Szczegóły kartoteki (z PESEL, dokumentem i rachunkiem) zapisuje audyt odczytów: kto, kiedy, którą
  kartotekę. Odczyt przez samego pracownika (konto powiązane z tą kartoteką) nie jest zapisywany.
- **R10.** Administrator widzi audyt odczytów kartoteki na jej stronie (zakładka „Odczyty”).
- **R11.** Kartotekę można usunąć tylko wtedy, gdy nie ma do niej żadnych dokumentów kadrowo-płacowych (od modułu
  004: brak umów albo upłynął okres ich przechowywania — [004 R18](../004-employment/spec.md)). Inaczej `U0003`. Okres przechowywania dokumentacji liczony od końca zatrudnienia
  i blokada usunięcia w tym okresie przychodzą z modułem 004, gdzie są daty końca umów. Usunięcie zostawia wpis audytu
  bez danych osobowych.

### Konto pracownika i profile
- **R12.** Kadry wiążą kartotekę z istniejącym kontem z rolą `Pracownik` (wyszukiwanym po e-mailu albo loginie).
  Kartoteka ma najwyżej jedno konto, a konto najwyżej jedną kartotekę w danej spółce. Powiązanie można usunąć.
- **R13.** `GET /api/auth/me` zwraca profile konta: kartoteka, spółka. Pracownik z dwiema kartotekami widzi dwa
  profile; z jedną — bez przełącznika.
- **R14.** Pracownik nie ma dostępu do listy kartotek ani do cudzych kartotek (`U0004`); swoje dane zobaczy w module
  dokumentów (010).

### Dane deweloperskie
- **R15.** W kontekście `dev` jest spółka „Demo”, kartoteka powiązana z kontem `pracownik` (bez PESEL — szyfrowanie
  wymaga klucza, którego Liquibase nie zna), żeby logowanie deweloperskie Pracownika pokazywało profil.
- **R16.** (dodane 2026-11-13, decyzja właściciela) Kartoteka ma **akronim**, który odróżnia osoby o tym samym imieniu
  i nazwisku. Przy założeniu baza nadaje go jako RRRRMM dnia dodania + 4 znaki z alfabetu bez mylących znaków (bez
  0/O, 1/I/L), np. `202611K7Q2`; Kadry mogą go zmienić (litery, cyfry, `.`, `-`, `_`, do 30 znaków). Unikalny w spółce
  bez względu na wielkość liter (`duplicate`). Lista pokazuje akronim i wyszukuje po nim.

## Przypadki brzegowe
- PESEL wpisany ze spacjami lub kreskami → zostają same cyfry.
- Osoba urodzona w 2000+ (miesiąc w PESEL + 20) — poprawna data z PESEL.
- Wyszukiwanie po 11 cyfrach, które nie są poprawnym PESEL → wyszukiwanie tekstowe po numerze, bez błędu.
- Zmiana klucza szyfrowania: poza zakresem (ADR-0009, konsekwencje).

## Poza zakresem 003
- Status kartoteki „aktywny/nieaktywny” wyliczany z umów — moduł 004.
- Rezydencja podatkowa inna niż polska.
- Zaproszenie e-mailem i zakładanie konta przez Kadry — konta zakłada Administrator (001).
- Okres retencji i jego źródło prawne — specyfikacja 004.
