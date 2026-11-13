# hr.web.app — produkt

## Co to jest

System Kadry i Płace dla grupy powiązanych spółek działających w Polsce. Kadry prowadzą w nim pracowników, umowy
(o pracę, zlecenia, o dzieło), czas pracy i nieobecności, a potem liczą listy płac, wydają paski i na koniec roku
generują PIT. Pracownik loguje się, żeby zobaczyć swoje paski, PIT-y i zgody. Wszystkie kwoty są w PLN, prawo
wyłącznie polskie. Funkcjonalnym wzorem jest moduł kadrowo-płacowy znanych systemów desktopowych (zakres i przepływ
pracy, nie wygląd).

## Dla kogo

| Rola | Co robi |
|---|---|
| Kadry | Kadry i płace w jednej roli: kartoteki, umowy, nieobecności, zgody, oświadczenia, listy płac, paski, PIT, PPK. Widzi wszystkie spółki. |
| Administrator | Użytkownicy, spółki, parametry prawne (stawki i progi z datą), słowniki. Widzi wszystkie spółki. |
| Pracownik | Tylko odczyt swoich pasków, PIT-ów, zgód i oświadczeń. Przy umowach w kilku spółkach ma kilka profili. |

## Cele („Done when”)

1. **Kartoteka i umowy** — Kadry zakładają pracownika w spółce i umowę o pracę, zlecenie albo dzieło z historią
   warunków (etat, stanowisko, wynagrodzenie, kody ZUS). Zmiana warunków nie zmienia przeszłości.
2. **Czas pracy i nieobecności** — kalendarz spółki daje wymiar miesiąca; choroba, urlop i nadgodziny trafiają na
   listę płac bez ręcznego przeliczania.
3. **Liczenie wypłat** — lista płac za miesiąc liczy w bazie brutto, składki ZUS pracownika i pracodawcy, zdrowotną,
   koszty, zaliczkę PIT (z PIT-2 i ulgami), PPK, potrącenia w kolejności i limitach z Kodeksu pracy oraz netto.
   Każda kwota ma ślad parametrów. Przypadki referencyjne z [modelu domeny](docs/domain/09-payroll.md) wychodzą
   co do grosza.
4. **Zamknięcie i korekta** — zamkniętej listy nie da się zmienić; błąd poprawia lista korygująca za ten sam okres.
5. **Dokumenty** — pasek wypłaty (PDF) dla każdej pozycji listy, zgoda na potrącenie do podpisu (PDF), PIT-11, PIT-4R
   i PIT-8AR jako PDF i XML zgodny ze schematem MF na dany rok.
6. **Bezpieczeństwo danych** — PESEL i rachunek zaszyfrowane, audyt zmian i odczytów, retencja dokumentacji
   pilnowana przez bazę, uprawnienia egzekwowane w bazie.
7. **Uruchomienie u każdego** — repo publiczne (MIT); `infrastructure/start.sh` stawia całość lokalnie, a logowanie
   deweloperskie jednym kliknięciem działa na każdą rolę tylko w Development.

## Non-goals (na teraz)

- Integracje z ATS i rekrutacja.
- Inne kraje i waluty.
- Wysyłka do ZUS (deklaracje ZUA, ZWUA, DRA, RCA, RSA — także jako pliki) i do MF (e-Deklaracje); system generuje
  PIT jako pliki do wysłania innym narzędziem.
- Przelewy bankowe i księgowanie list płac.
- Wymiana plików z instytucją finansową PPK.
- Deploy produkcyjny (osobne zadanie).

## Zasady, których nie zmieniamy bez ADR

- Logika w bazie: zapis procedurą, odczyt funkcją, rola API bez praw do tabel ([ADR-0003](docs/adr/0003-logic-in-database.md)).
- Endpointy pisane ręcznie, OpenAPI z kodu, klient orval ([ADR-0005](docs/adr/0005-http-contract-and-openapi.md)).
- Parametry prawne nie są w kodzie: tabela z okresem obowiązywania i źródłem, naliczenie wybiera wartość według daty.
- Testy na prawdziwym PostgreSQL ([ADR-0006](docs/adr/0006-tests-on-real-postgresql.md)).

## Kolejne specyfikacje (SDD)

Kolejność modułów wynika z zależności w [modelu domeny](docs/domain/README.md). Każdy moduł przechodzi
`docs/specs/NNN-<moduł>/spec.md` → `plan.md` → `tasks.md`, a każdy etap zatwierdza właściciel.

| Nr | Moduł | Stan |
|---|---|---|
| 001 | Platforma: spółki, użytkownicy, role, logowanie (w tym deweloperskie), audyt, parametry prawne | zrobione (bez OIDC) |
| 002 | Słowniki: stanowiska, kody ZUS, urzędy skarbowe | zrobione |
| 003 | Pracownicy: kartoteka, adresy, rachunki, szyfrowanie, profile | zrobione |
| 004 | Zatrudnienie: umowy i wersje warunków, status i okres przechowywania | zrobione |
| 005 | Oświadczenia podatkowe | zrobione |
| 006 | Składniki, zgody na potrącenia, tytuły egzekucyjne | specyfikacja do zatwierdzenia |
| 007 | Czas pracy i nieobecności | do napisania |
| 008 | PPK | do napisania |
| 009 | Listy płac, liczenie, korekty | do napisania |
| 010 | Paski, dokumenty zgód, PIT | do napisania |

Pomysły spoza zakresu dopisuje się tu jako kolejne wiersze, a nie do kodu.
