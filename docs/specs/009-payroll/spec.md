# 009 Listy płac — specyfikacja

Model: [09-payroll.md](../../domain/09-payroll.md). Decyzje właściciela: liczy baza (wymagania 7–8), jedna zwykła
lista na spółkę, rodzaj umowy i miesiąc oraz listy korygujące (decyzja 7.3.2), ślad każdej pozycji (wymaganie 8).
Właściciel 2026-11-13: „rób bez pytania” — decyzje projektowe oznaczone **[D]**, sprawy niepotwierdzone
w przepisach **[?]**.

Podstawa prawna (stan na 13.11.2026): ustawa o PIT Dz.U. 2026 poz. 592; Ordynacja podatkowa Dz.U. 2026 poz. 622;
Kodeks pracy Dz.U. 2026 poz. 1245; ustawa zasiłkowa Dz.U. 2026 poz. 854; ustawa o systemie ubezpieczeń społecznych
Dz.U. 2026 poz. 1474; ustawa o świadczeniach opieki zdrowotnej Dz.U. 2025 poz. 1461; rozporządzenie o wynagrodzeniu
za czas niewykonywania pracy Dz.U. 2017 poz. 927 („R1996”); rozporządzenie urlopowe Dz.U. 1997 nr 2 poz. 14 ze zm.
(„R1997”); ustawa o rynku pracy Dz.U. 2025 poz. 620; ustawa o FGŚP Dz.U. 2026 poz. 186; ustawa budżetowa 2026
Dz.U. 2026 poz. 62; ustawa o PPK Dz.U. 2026 poz. 192.

## Cel

Kadry zakładają listę płac spółki za miesiąc (umowy o pracę, zlecenia, dzieła) albo listę korygującą, naliczają ją
— baza liczy dla każdej umowy pozycje (przychody, składki, koszty, zaliczkę PIT, PPK, potrącenia, netto, do wypłaty)
ze śladem źródeł — sprawdzają i zamykają. Zamknięcie blokuje dane okresu i aktualizuje sumy roczne.

## Wymagania

### Lista płac
- **R1.** Lista: spółka, rodzaj (`employment`, `mandate`, `specificWork`, `correction`), okres (miesiąc), data wypłaty,
  numer (kolejny w spółce, np. `2026/11/UP`). Zwykła lista — jedna na spółkę, rodzaj i miesiąc (`duplicate`). Data
  wypłaty nie później niż 10. dzień następnego miesiąca (KP art. 85 § 2) — ostrzeżenie, nie błąd **[D]**.
- **R2.** Statusy: `open` → `calculated` → `closed`. Naliczenie można powtórzyć (wraca do `calculated` z nowymi
  pozycjami); zmiana danych źródłowych przy `calculated` oznacza listę jako nieaktualną (`stale`) **[D]**. Zamkniętej
  listy nie zmienia się — tylko lista korygująca. Otwartą listę można usunąć.
- **R3.** Skład: umowy danego rodzaju spółki, które trwają choć jeden dzień w okresie (dla zlecenia godzinowego —
  także, gdy są potwierdzone godziny w okresie, 007 R17). Kadry mogą wyłączyć umowę z listy przed naliczeniem.
- **R4.** Lista korygująca: wskazuje zamkniętą listę i umowy; liczy pozycje okresu korygowanego na nowo z obecnych
  danych i zapisuje **różnice**; ma własną datę wypłaty (miesiąc podatkowy PIT), a składki należą do okresu listy
  korygowanej. Lista korygowana zostaje nietknięta.

### Przychody (umowa o pracę)
- **R5.** Wynagrodzenie zasadnicze z warunków (004) obowiązujących w okresie; zmiana warunków w miesiącu — części
  proporcjonalne do dni kalendarzowych **[D]**. Zatrudnienie przez część miesiąca: wynagrodzenie ÷ godziny wymiaru
  miesiąca × godziny wymiaru w okresie umowy (R1996 § 12 ust. 2).
- **R6.** Choroba (007): za dni choroby pomniejszenie zasadniczego o 1/30 za każdy dzień (R1996 § 11); pełny miesiąc
  choroby — zasadnicze 0 **[?]** (praktyka). Wynagrodzenie chorobowe (KP art. 92) do 33 dni w roku, 14 dni po roku,
  w którym pracownik skończył 50 lat — dni liczone z nieobecności tej kartoteki w roku **[D]** (dni u poprzedniego
  pracodawcy — pole w limicie urlopu? nie: poza zakresem, ostrzeżenie); dalej zasiłek chorobowy (ustawa zasiłkowa
  art. 11) wypłacany przez płatnika, gdy spółka ma ustawione „wypłaca zasiłki” (art. 61: powyżej 20 ubezpieczonych
  — ustawienie Kadr **[D]**), inaczej pozycja informacyjna „zasiłek — ZUS”.
- **R7.** Podstawa chorobowego/zasiłku (art. 36): średnia z 12 miesięcy kalendarzowych przed miesiącem niezdolności
  z zamkniętych list tej kartoteki — przychód podlegający składkom minus faktycznie potrącone składki emerytalna,
  rentowa i chorobowa (art. 3 pkt 3), z pominięciem miesięcy z przepracowaną mniej niż połową czasu (art. 38 ust. 2)
  **[D: bez uzupełniania miesięcy]**; bez zamkniętych list — wynagrodzenie z warunków umowy pomniejszone o składki
  pracownika (art. 37) **[D]**. Minimum przy pełnym etacie: minimalne wynagrodzenie − 13,71% (art. 45; 2026:
  4 147,10 zł) × etat. Dzienna = podstawa ÷ 30 × 80% (100% dla CHW i CHC). Ta sama podstawa w kolejnych niezdolnościach,
  gdy przerwa < 1 miesiąca kalendarzowego (art. 43).
- **R8.** Urlop i inne płatne nieobecności z wynagrodzeniem (UW, UZ, UO, OD): składniki stałe bez zmian; składniki
  zmienne (nadgodziny, składniki `percentOfBase` i `rateTimesHours`) — średnia z 3 zamkniętych miesięcy ÷ godziny
  przepracowane × godziny nieobecności (R1997 § 8–9) **[D: bez wydłużania do 12 miesięcy]**. SW (siła wyższa): 50%
  wynagrodzenia za godziny nieobecności. UB, UOP, NN — pomniejszenie zasadniczego: wynagrodzenie ÷ wymiar godzin
  miesiąca × godziny nieobecności (R1996 § 12 ust. 1).
- **R9.** Nadgodziny (007 R16): stawka = zasadnicze ÷ wymiar godzin miesiąca (R1996 § 4a) × godziny × (1 + 0,5)
  albo × (1 + 1,0) **[?]** (normalne wynagrodzenie + dodatek).
- **R10.** Składniki umowy (006): kwota stała; procent zasadniczego; stawka × godziny (godziny z 007 R17 dla zlecenia,
  dla umowy o pracę — poza zakresem **[D]**). Cechy składnika (ZUS, zdrowotna, PIT, chorobowe) decydują o podstawach.
- **R11.** Ekwiwalent za niewykorzystany urlop w miesiącu rozwiązania umowy (KP art. 171, R1997 § 18–19): saldo
  godzin (007 R15) × (zasadnicze + średnia zmiennych) ÷ (współczynnik × etat) ÷ 8 (przy niepełnym etacie współczynnik
  obniżony proporcjonalnie — R1997 § 19 ust. 3; saldo w godzinach dnia 8-godzinnego); współczynnik jako parametr prawny
  (2026: 20,92 — 251 ÷ 12 **[?]** zaokrąglenie). Ekwiwalent jest przychodem ze składkami i podatkiem.

### Przychody (zlecenie, dzieło)
- **R12.** Zlecenie: kwota z warunków (ryczałt) albo stawka × potwierdzone godziny w okresie (007 R17), plus składniki.
  Dzieło: kwota z warunków w miesiącu wypłaty (umowa kończy się w okresie listy) **[D]**.

### Składki (pracownik / pracodawca)
- **R13.** Podstawa składek społecznych: przychód ze składników z cechą ZUS (bez wynagrodzenia chorobowego
  i zasiłku). Emerytalna 9,76% / 9,76%, rentowa 1,5% / 6,5% — do limitu 30-krotności w roku (2026: 282 600 zł;
  ustawa o sus art. 19), liczonego z sum okresów zamkniętych list kartoteki; chorobowa 2,45% (pracownik);
  wypadkowa — stopa spółki z okresem (domyślnie 1,67%). Ubezpieczenia według warunków umowy (004 R12). Dzieło —
  bez składek.
- **R14.** FP + FS 2,45% łącznie (UB2026 art. 25–26: 1,0% + 1,45%; FS art. 5 ust. 2) od podstawy bez limitu, gdy
  podstawa w miesiącu ≥ minimalne wynagrodzenie (bez przeliczania na etat — ustawa o rynku pracy art. 259); nie
  płaci się za kobiety od 55 i mężczyzn od 60 lat (art. 261) — płeć z PESEL, bez PESEL płaci się (ostrzeżenie) **[D]**.
  FGŚP 0,10% bez progu, te same zwolnienia wiekowe (ustawa o FGŚP art. 9b). Pozostałe zwolnienia (art. 262–263,
  9a, 9c) — znacznik na umowie poza zakresem **[D]**.
- **R15.** Składka zdrowotna 9% od podstawy = podstawa emerytalno-rentowa (bez limitu) + wynagrodzenie chorobowe
  − składki społeczne pracownika (ustawa o świadczeniach art. 81 ust. 1 i 6). Ograniczenie do zaliczki z 2021 r.
  (art. 83 ust. 1) — **nie liczone [?]**.

### Podatek
- **R16.** Umowa o pracę: dochód = przychód opodatkowany − składki społeczne pracownika (od przychodu
  opodatkowanego) − koszty (250 / 300 zł z warunków `standard` / `increased` albo oświadczenia `increasedCosts`, 005;
  brak przy `employeeCostsWaiver`; koszty tylko do części opodatkowanej — art. 22 ust. 3b); podstawa zaokrąglona do
  zł; 12% do 120 000 zł dochodu narastająco u płatnika, 32% od nadwyżki (art. 32 ust. 2); minus kwota zmniejszająca
  z PIT-2 (1/12, 1/24, 1/36 z 3 600 zł) raz w miesiącu podatkowym — na pierwszej liście z datą wypłaty w miesiącu
  **[D]**; zaliczka zaokrąglona do zł (OP art. 63), nie mniej niż 0. `higherRate` → 32%; `noAdvances` → 0 do 30 000
  zł dochodu u płatnika (art. 31c). Miesiąc podatkowy = miesiąc daty wypłaty.
- **R17.** Ulga dla młodych (art. 21 ust. 1 pkt 148): przychód z umowy o pracę i zlecenia osoby, która w dniu wypłaty
  nie ukończyła 26 lat, zwolniony do 85 528 zł w roku (wspólnie z ulgami 152–154 z oświadczeń 005, art. 21 ust. 44);
  nie stosuje się przy `youthReliefWaiver`. Ulgi 152–154 (oświadczenia `returnRelief`, `familyRelief`,
  `seniorRelief`) — ten sam mechanizm i limit.
- **R18.** Zlecenie: zaliczka 12% (art. 41 ust. 1) od dochodu = przychód − składki − koszty 20% (od przychodu minus
  składki społeczne, art. 22 ust. 9 pkt 4) albo 50% (do 120 000 zł rocznie); kwota zmniejszająca z PIT-2. Ryczałt
  12% od przychodu (art. 30 ust. 1 pkt 5a), gdy kwota w umowie ≤ 200 zł i zleceniobiorca nie jest pracownikiem
  spółki. Dzieło: koszty 20/50% od przychodu, zaliczka 12%, ryczałt jak wyżej.
- **R19.** Wpłata pracodawcy na PPK jest przychodem pracownika do PIT w miesiącu listy **[?]** (bez składek ZUS
  i zdrowotnej — ustawa o PPK art. 26 ust. 5).

### PPK i potrącenia
- **R20.** PPK (008 `rates_on` w dniu wypłaty): podstawa = podstawa emerytalno-rentowa bez limitu 30-krotności;
  wpłaty pracownika i pracodawcy, podstawowe i dodatkowe; obniżona podstawowa pracownika pomijana, gdy wynagrodzenie
  w spółce > 1,2 × minimalne (art. 27 ust. 5). Wpłata pracownika odliczana z netto (art. 27 ust. 9).
- **R21.** Netto = przychód − składki społeczne pracownika − zdrowotna − zaliczka PIT − PPK pracownika.
- **R22.** Potrącenia (006) w kolejności art. 87 § 1–2 z limitami § 3–4 i kwotami wolnymi art. 87¹ (proporcjonalnie
  do etatu): alimenty (3/5, bez kwoty wolnej), egzekucja inna (1/2, kwota wolna 100% — parametr z PPK albo bez),
  zaliczki pieniężne (1/2 łącznie z egzekucją, wolna 75%), kary (1/10 po potrąceniach 1–3, wolna 90%), dobrowolne
  (art. 91: 100% na rzecz pracodawcy, 80% innych; w kwocie ze składnika, nie więcej niż zgoda). Zajęcia: alimenty
  przed innymi, potem data doręczenia; nie więcej niż pozostało do ściągnięcia. Zlecenie: limity art. 87 i 87¹ dla
  świadczeń powtarzających się (KPC art. 833 § 2¹) **[D: stosowane do każdego zlecenia]**, kwota wolna bez etatu.
- **R23.** Do wypłaty = netto − potrącenia; nie mniej niż 0.

### Ślad, zamknięcie, sumy
- **R24.** Każda pozycja ma kod, stronę (`income`, `employee`, `employer`, `info`, `deduction`), podstawę, stawkę,
  kwotę i ślad (JSON: parametry prawne z datą, warunki, nieobecności, godziny, oświadczenia, PPK, zajęcia, sumy roczne
  przed naliczeniem). Kwoty zaokrąglane do grosza w każdej pozycji, PIT do złotych.
- **R25.** Zamknięcie: tylko lista `calculated` i nie `stale`; blokuje zmiany nieobecności, nadgodzin, godzin zlecenia,
  składników i oświadczeń w okresie zamkniętej listy (`U0003` `payrollClosed`) oraz usuwanie umowy, zgody, zajęcia
  i uczestnictwa PPK użytych na liście (spełnia „od 009” z 004–008). Sumy roczne kartoteki (podstawa emerytalno-
  rentowa po okresie, dochód PIT po dacie wypłaty, przychód zwolniony, koszty autorskie, kwota ściągnięta z zajęć)
  liczone z zamkniętych list.
- **R26.** Role: Kadry i Administrator. Pracownik — brak dostępu do list (pasek w 010). Audyt zmian list.
- **R27.** Dane deweloperskie: brak list (lista powstaje z ekranu) **[D]**.

## Przypadki brzegowe
- Anna, XI 2026, pełny etat 7 500 zł, choroba 16–20.11 (5 dni), 4 nadgodziny 50%: zasadnicze − 5/30, chorobowe
  80% z podstawy, NADG50 4 × 7 500/160 × 1,5 = 281,25 zł; wymiar listopada 160 h.
- Przekroczenie 120 000 zł w grudniu: część 12%, nadwyżka 32%.
- Ulga dla młodych: składki i zdrowotna naliczane, zaliczka 0 do limitu.
- Lista korygująca za październik wypłacona 10.12: składki październikowe, PIT grudniowy, bez drugiej kwoty
  zmniejszającej w grudniu, jeśli grudniowa zwykła lista ją już użyła.

## Poza zakresem 009
- Pasek, PIT-11, PIT-4R (010); deklaracje ZUS, przelewy, księgowanie (non-goals).
- Ograniczenie zdrowotnej do zaliczki z 2021 r.; uzupełnianie podstawy chorobowego; podstawa urlopowa z 12 miesięcy;
  dni chorobowe u poprzedniego pracodawcy; zwolnienia z FP/FGŚP inne niż wiek; zasiłki inne niż chorobowy.
- Import historii wynagrodzeń sprzed systemu (podstawa chorobowego z warunków — R7).
