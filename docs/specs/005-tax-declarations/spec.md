# 005 Oświadczenia podatkowe — specyfikacja

Model: [05-tax-declarations.md](../../domain/05-tax-declarations.md). Decyzje właściciela: rejestr PIT-2 i oświadczeń
z datą od–do (wymaganie 11, decyzja 6), oświadczenia na papierze rejestrowane w systemie
([założenie 7.2.4](../../domain/README.md#72-założenia-modelu)), jedna lista w miesiącu
([decyzja 7.3.2](../../domain/README.md#73-decyzje-do-pytań-otwartych-2026-11-12)).

Podstawa prawna: ustawa o podatku dochodowym od osób fizycznych, tekst jednolity Dz.U. 2026 poz. 592 (stan na
1.04.2026; późniejsze zmiany do 13.11.2026 nie dotyczą przepisów poniżej), dalej „u.p.d.o.f.”; Ordynacja podatkowa,
Dz.U. 2026 poz. 622. Numeracja według tego tekstu: zasady ogólne w art. 31a, PIT-2 w art. 31b, niepobieranie zaliczek
w art. 31c.

## Cel

Kadry rejestrują w kartotece oświadczenia i wnioski pracownika, które zmieniają naliczenie zaliczki PIT. Każdy wpis
ma rodzaj, datę złożenia i miesiące, w których obowiązuje. Lista płac (009) czyta wpisy obowiązujące w miesiącu
wypłaty. Pracownik widzi swoje oświadczenia w samoobsłudze.

## Wymagania

### Rodzaje
- **R1.** Rodzaje (`kind`) z podstawą prawną:

  | `kind` | Co | Podstawa | Szczegóły | Po końcu umowy |
  |---|---|---|---|---|
  | `pit2` | oświadczenie o stosowaniu pomniejszenia zaliczki (PIT-2) | art. 31b ust. 1–3 | `share`: `1/12`, `1/24`, `1/36` | wygasa |
  | `youthReliefWaiver` | wniosek o obliczanie zaliczek bez ulgi dla młodych | art. 32 ust. 6, art. 41 ust. 1b | — | obowiązuje |
  | `returnRelief` | oświadczenie o uldze na powrót | art. 21 ust. 1 pkt 152, art. 31a ust. 8 | `yearFrom`, `yearTo` | wygasa |
  | `familyRelief` | oświadczenie o uldze dla rodzin 4+ | art. 21 ust. 1 pkt 153, art. 31a ust. 8 | — | wygasa |
  | `seniorRelief` | oświadczenie o uldze dla pracujących seniorów | art. 21 ust. 1 pkt 154, art. 31a ust. 8 | — | wygasa |
  | `increasedCosts` | oświadczenie o miejscu zamieszkania (podwyższone koszty) | art. 22 ust. 2 pkt 3, art. 32 ust. 7 | — | wygasa |
  | `employeeCostsWaiver` | wniosek o obliczanie zaliczek bez miesięcznych kosztów pracowniczych | art. 32 ust. 6 | — | obowiązuje |
  | `authorCostsWaiver` | wniosek o rezygnację z kosztów 50% | art. 32 ust. 8, art. 41 ust. 11 | — | obowiązuje |
  | `noAdvances` | wniosek o niepobieranie zaliczek w roku | art. 31c ust. 1–2 | — | wygasa |
  | `higherRate` | wniosek o zaliczki według wyższej stawki skali | art. 41a | — | wygasa |

  Ulga dla młodych działa bez oświadczenia (płatnik z art. 32 i art. 41 ust. 1 stosuje ją z mocy prawa), dlatego jest
  tylko wniosek o jej niestosowanie. Kolumna „po końcu umowy” wynika z art. 31a ust. 7 (R7).
- **R2.** Szczegóły zależnie od rodzaju: `share` wymagany dla `pit2`; `yearFrom` i `yearTo` wymagane dla
  `returnRelief` (`yearFrom ≤ yearTo`, inaczej `beforeFrom` na `yearTo`). Szczegół innego rodzaju → `invalid`.
  Liczby lat ulgi na powrót system nie sprawdza: to fakt z oświadczenia, za który odpowiada pracownik (art. 31a ust. 9
  i 10).

### Okres obowiązywania
- **R3.** Kadry i Administrator wpisują oświadczenie w kartotece: rodzaj, szczegóły, data złożenia (`submittedAt`,
  nie z przyszłości → `future`) i miesiąc, od którego obowiązuje (`validFrom`, pierwszy dzień miesiąca). Płatnik
  stosuje oświadczenie najpóźniej od miesiąca po miesiącu otrzymania (art. 31a ust. 5), może wcześniej. Dlatego
  `validFrom` to miesiąc złożenia albo następny; inny miesiąc → `outsideAllowed` na `validFrom`. Domyślnie front
  podpowiada miesiąc następny.
- **R4.** Koniec (`validTo`) to ostatni dzień miesiąca albo brak (oświadczenie obowiązuje także w kolejnych latach,
  art. 31a ust. 6). Wyjątki:
  - `noAdvances` obowiązuje tylko w roku złożenia (art. 31c ust. 1: „w danym roku podatkowym”) — `validTo` ustawia
    baza na 31 grudnia roku `validFrom`; późniejszy → `invalid`. Rok `validFrom` musi być rokiem złożenia
    (`outsideAllowed`), więc wniosek złożony w grudniu obowiązuje tylko od grudnia;
  - `returnRelief` — `validFrom` w latach `yearFrom`–`yearTo` (`outsideAllowed`), a `validTo` domyślnie
    31 grudnia `yearTo` i nie później.
- **R5.** Dwa wpisy tego samego rodzaju w jednej kartotece nie nakładają się okresami (`overlap` na `validFrom`).
  Ograniczenia między płatnikami (PIT-2 najwyżej u trzech płatników, łącznie nie więcej niż 1/12 w miesiącu, art. 31b
  ust. 2 i 5) dotyczą pracownika i system ich nie sprawdza — każda spółka jest osobnym płatnikiem.
- **R6.** Wycofanie albo zmiana następuje nowym oświadczeniem lub wnioskiem (art. 31a ust. 3–4). Kadry „kończą”
  wpis: data złożenia wycofania i miesiąc, od którego wpis już nie obowiązuje (ta sama reguła co w R3), a baza
  ustawia `validTo` na ostatni dzień miesiąca poprzedniego. Zmiana udziału PIT-2 = zakończenie i nowy wpis od
  tego samego miesiąca. Koniec nie może wypaść przed `validFrom` (`beforeFrom`). Zakończenie można cofnąć.
- **R7.** Po ustaniu stosunku prawnego płatnik pomija oświadczenia i wnioski, poza wnioskami z art. 32 ust. 6 i 8
  oraz art. 41 ust. 11 (art. 31a ust. 7). Baza wylicza `lapsesOn` dla rodzajów, które „wygasają” (R1): stosunek
  prawny to ciągły okres umów kartoteki (dowolnego rodzaju; przerwa co najmniej jednego dnia go kończy), w którym
  wpis zaczął obowiązywać, a gdy w `validFrom` umowy nie ma — pierwszy taki okres zaczynający się później.
  `lapsesOn` to ostatni dzień tego okresu; brak umów albo umowa bez końca → brak `lapsesOn`. Nowa umowa po przerwie
  wymaga nowego oświadczenia.
- **R8.** Status wpisu na dziś: `future` (przed `validFrom`), `active`, `ended` (po `validTo`), `lapsed` (po
  `lapsesOn`).

### Zapis i odczyt
- **R9.** Treść wpisu (rodzaj, szczegóły, daty złożenia i od) jest niezmienna. Pomyłkę poprawia się usunięciem
  i nowym wpisem. Usuwanie i kończenie z wersją wiersza (`U0003` przy nieaktualnej). Od modułu 009 usunięcie wpisu
  użytego na liście płac będzie blokowane.
- **R10.** Lista oświadczeń kartoteki: rodzaj, szczegóły, data złożenia, od, do, `lapsesOn`, status; filtr „tylko
  obowiązujące”.
- **R11.** Rutyna dla listy płac (wewnętrzna, bez endpointu): wpisy kartoteki obowiązujące w danym dniu (z `validTo`
  i `lapsesOn`). Korzysta z niej 009.
- **R12.** Każda zmiana jest w audycie zmian. Oświadczenia nie zawierają kwot ani danych szczególnych, więc odczyt
  nie trafia do audytu odczytów (inaczej niż warunki umowy w 004 R19).

### Samoobsługa i role
- **R13.** Pracownik widzi w „Moich dokumentach” oświadczenia swoich profili (rodzaj, szczegóły, daty, status) — tylko
  odczyt. Cudza kartoteka → `U0004`.
- **R14.** Pracownik nie zapisuje oświadczeń (`U0004`); składa je na papierze (założenie 7.2.4).

### Okres przechowywania
- **R15.** Płatnik przechowuje dokumenty poboru do przedawnienia zobowiązania płatnika (Ordynacja art. 32 § 1):
  5 lat od końca roku, w którym minął termin płatności (art. 70 § 1). Oświadczenia są częścią kartoteki i znikają
  tylko razem z nią, a okres przechowywania kartoteki (004 R17, co najmniej 10 lat od końca roku ustania umowy)
  jest dłuższy. Osobnej blokady w 005 nie ma; wypłaty po końcu umowy (wnioski „obowiązujące” z R1) sprawdzi 009.

### Dane deweloperskie
- **R16.** W kontekście `dev` „Anna Testowa” (Demo) ma PIT-2 z udziałem 1/12, złożony 2025-12-15, od 2026-01-01.

## Przypadki brzegowe
- PIT-2 złożony 20.03.2026 → `validFrom` 2026-03-01 albo 2026-04-01; 2026-05-01 → `outsideAllowed`.
- `noAdvances` złożony 10.12.2026 od 2027-01-01 → `outsideAllowed` (wniosek dotyczy roku złożenia, R4); dozwolony
  tylko 2026-12-01, do 2026-12-31.
- Zmiana PIT-2 z 1/12 na 1/24 od 2026-05-01: zakończenie (do 2026-04-30) i nowy wpis od 2026-05-01 nie nakładają
  się.
- Umowa o pracę do 30.06.2026, zlecenie od 01.07.2026 → jeden ciągły stosunek: PIT-2 nie wygasa 30.06. Zlecenie od
  02.07.2026 → `lapsesOn` 2026-06-30.
- PIT-2 złożony przed pierwszą umową (nowy pracownik) → `lapsesOn` z pierwszej umowy.
- `youthReliefWaiver` po rozwiązaniu umowy dalej `active`.

## Poza zakresem 005
- Naliczenie zaliczki z oświadczeń, limity roczne (85 528 zł ulg, 30 000 zł dla `noAdvances`, 120 000 zł kosztów 50%)
  i podwyższone koszty przy kilku umowach (art. 22 ust. 2 pkt 4) — moduł 009.
- Wnioski dla organu rentowego (PIT-2A, art. 34) i zasiłek macierzyński z ulgą dla młodych — system nie jest organem
  rentowym.
- Wniosek o niestosowanie kosztów 20% przy zleceniu — przepisu nie znaleziono (art. 41 ust. 1b odsyła do art. 32
  ust. 6, który dotyczy kosztów pracowniczych); do potwierdzenia przed 009.
- Składanie oświadczeń przez pracownika w systemie i podpis elektroniczny (założenie 7.2.4).
- Wydruk wzoru PIT-2 (010).
