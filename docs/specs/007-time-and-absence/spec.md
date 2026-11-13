# 007 Czas pracy i nieobecności — specyfikacja

Model: [07-time-and-absence.md](../../domain/07-time-and-absence.md). Decyzje właściciela: osobny moduł czasu pracy
(decyzja 4: kalendarze, wymiar, nieobecności, ewidencja), lista płac czyta dni i godziny, ale ich nie zmienia.

Podstawa prawna (stan na 13.11.2026): Kodeks pracy, t.j. Dz.U. 2026 poz. 1245 („KP”); ustawa o dniach wolnych od
pracy, t.j. Dz.U. 2025 poz. 296 (Wigilia wolna od 1.02.2025 — Dz.U. 2024 poz. 1965); ustawa zasiłkowa, t.j. Dz.U. 2026
poz. 854; ustawa o minimalnym wynagrodzeniu, t.j. Dz.U. 2024 poz. 1773; rozporządzenie o dokumentacji pracowniczej,
t.j. Dz.U. 2026 poz. 474; rozporządzenie o usprawiedliwianiu nieobecności, Dz.U. 2014 poz. 1632.

## Cel

Kadry prowadzą kalendarz pracy spółki, nieobecności pracowników (urlopy, choroby, zwolnienia), roczne limity urlopu
oraz ewidencję nadgodzin i godzin zlecenia. Z kalendarza i etatu wynika wymiar czasu pracy w miesiącu, a z
nieobecności — dni i godziny, które lista płac (009) wyceni. **Wyceny tu nie ma**: wynagrodzenie urlopowe,
chorobowe, zasiłki i dodatki za nadgodziny liczy 009.

## Wymagania

### Święta i kalendarz
- **R1.** Słownik świąt ustawowych (data, nazwa) z ustawy o dniach wolnych art. 1 pkt 1 — w tym 24 grudnia od 2025 r.
  Migracja zakłada lata 2026 i 2027 (święta ruchome: Wielkanoc 5.04.2026 / 28.03.2027, Zielone Świątki 24.05 / 16.05,
  Boże Ciało 4.06 / 27.05). Administrator dopisuje kolejne lata ręcznie albo przyciskiem **Pobierz święta ustawowe**:
  system wylicza listę z ustawy (10 dat stałych + Wielkanoc, Poniedziałek Wielkanocny, Zielone Świątki +49 dni,
  Boże Ciało +60 dni; Wielkanoc algorytmem kalendarza gregoriańskiego) bez zewnętrznego serwisu i dopisuje tylko
  brakujące daty (istniejące wpisy, także ręczne, zostają; każde dodanie w audycie). Lata przed 2025 (inna lista —
  bez Wigilii) — `U0003` `statutoryHolidaysUnsupported`, wpis ręczny. Zmiana ustawy wymaga zmiany
  `time_absence.statutory_holidays` i jej testu.
- **R2.** Kalendarz spółki na rok (jeden na spółkę i rok, decyzja projektowa — systemy równoważne i zmianowe poza
  zakresem): Kadry go **generują**: poniedziałek–piątek dzień roboczy 8 h, sobota i niedziela wolne (0 h), święto 0 h.
  Generowanie roku, który ma już kalendarz, odrzuca `U0003` `calendarExists`.
- **R3.** Święto w sobotę obniża wymiar o 8 h (KP art. 130 § 2): przy generowaniu kalendarz pokazuje, ile dni wolnych
  za sobotnie święta trzeba wyznaczyć (2026: 15.08, 26.12; 2027: 1.05, 25.12). Kadry zmieniają wybrany dzień roboczy
  na `designatedOff` (dzień wolny za święto w sobotę) w tym samym miesiącu. Ostrzeżenie (nie błąd), gdy w miesiącu
  z sobotnim świętem brak wyznaczonego dnia — wymiar miesiąca jest wtedy o 8 h za wysoki.
- **R4.** Dzień kalendarza można zmienić (rodzaj: `workday`, `dayOff`, `holiday`, `designatedOff`; godziny 0–12 dla
  dnia roboczego, 0 dla pozostałych). Święta ze słownika nie da się zrobić dniem roboczym (`invalid`).
- **R5.** Wymiar czasu pracy umowy o pracę w miesiącu = suma godzin dni roboczych kalendarza spółki w okresie umowy
  w tym miesiącu × etat z warunków obowiązujących danego dnia (KP art. 130; przeliczenie przez etat to praktyka —
  przepis nie podaje wzoru dla niepełnego etatu). Wynik w godzinach z dokładnością do 0,01. Funkcja dla 009 i ekran
  „wymiar” w kartotece.

### Rodzaje nieobecności
- **R6.** Słownik systemowy (tylko do odczytu), z podstawą:

  | Kod | Nazwa | Płatność (wycenia 009) | Limit | Podstawa |
  |---|---|---|---|---|
  | `UW` | Urlop wypoczynkowy | urlopowe | limit urlopu | KP art. 152–154 |
  | `UZ` | Urlop na żądanie | urlopowe | limit urlopu, najwyżej 4 dni w roku | KP art. 167² |
  | `UO` | Urlop okolicznościowy | wynagrodzenie | 1 albo 2 dni na zdarzenie | rozp. 2014 § 15 |
  | `OD` | Opieka nad dzieckiem do 14 lat | wynagrodzenie | 2 dni albo 16 h w roku | KP art. 188 |
  | `SW` | Zwolnienie — siła wyższa | 50% wynagrodzenia | 2 dni albo 16 h w roku | KP art. 148¹ |
  | `UOP` | Urlop opiekuńczy | bezpłatny | 5 dni w roku | KP art. 173¹ |
  | `CH` | Choroba (L4) | chorobowe / zasiłek 80% | — | KP art. 92, ust. zasiłkowa art. 11 |
  | `CHW` | Choroba — wypadek w drodze do/z pracy | chorobowe / zasiłek 100% | — | KP art. 92 § 1 pkt 2 |
  | `CHC` | Choroba w ciąży | chorobowe / zasiłek 100% | — | KP art. 92 § 1 pkt 2 |
  | `UB` | Urlop bezpłatny | bezpłatny | — | KP art. 174 |
  | `NN` | Nieobecność nieusprawiedliwiona | bezpłatna | — | — |

  Urlopy rodzicielskie, wychowawczy i zasiłek opiekuńczy (ZUS) — poza zakresem 007 (osobna specyfikacja).

### Nieobecności
- **R7.** Kadry rejestrują nieobecność umowy: rodzaj, od–do (włącznie), dla choroby numer zaświadczenia e-ZLA
  (wymagany, tekst do 20 znaków — format serii i numeru niepotwierdzony w przepisach), uwagi. Okres w umowie
  (`outsidePeriod`); nieobecności jednej umowy się nie nakładają (`overlap`).
- **R8.** Rodzaje dozwolone dla umowy: umowa o pracę — wszystkie; zlecenie — tylko `CH`, `CHW`, `CHC` i tylko gdy
  warunki zlecenia w dniu początku mają ubezpieczenie chorobowe (`invalid` na `absenceTypeId`); dzieło — żadne.
- **R9.** Baza wylicza: dni kalendarzowe (choroba jest płatna za każdy dzień, KP art. 92 § 2) i godziny pracy, które
  nieobecność zabrała (dni robocze kalendarza × etat, KP art. 154² — 1 dzień = 8 h × etat).
- **R10.** Limity roczne w dniach albo godzinach (R6): przekroczenie → `limitExceeded` na `dateTo` z liczbą
  pozostałą. `OD` i `SW`: sposób (dni albo godziny) wybiera pracownik przy pierwszym wniosku w roku — w 007 limit
  liczony w dniach; zwolnienia godzinowe poza zakresem (decyzja projektowa).
- **R11.** Urlop (`UW`, `UZ`) nie może przekroczyć pozostałego limitu urlopu roku (R13) — `leaveLimitExceeded`.
- **R12.** Nieobecność można zmienić i usunąć (z wersją wiersza); od 009 — blokada w okresie zamkniętej listy płac.

### Limit urlopu wypoczynkowego
- **R13.** Limit umowy o pracę na rok (w godzinach): baza **proponuje** wymiar z KP art. 154 i 155¹–155²ᵃ — 20 albo
  26 dni (wybór Kadr: staż urlopowy, w tym wykształcenie z art. 155 i okresy z art. 302¹ od 1.05.2026, liczą Kadry
  z dokumentów), × 8 h × etat (niepełny dzień w górę do pełnego dnia, art. 154 § 2), proporcjonalnie do miesięcy
  umowy w roku (1/12 za miesiąc, niepełny miesiąc w górę, art. 155²ᵃ), gdy umowa nie trwa cały rok. Pierwsza praca
  w życiu (1/12 po każdym miesiącu, art. 153 § 1) — znacznik ustawiany przez Kadry.
- **R14.** Kadry mogą poprawić wyliczony limit z uzasadnieniem (np. urlop wykorzystany u poprzedniego pracodawcy,
  zmiana etatu w trakcie roku — przepis tego nie reguluje). Urlop zaległy = niewykorzystany limit poprzedniego roku
  (wyliczany, z możliwością korekty); ostrzeżenie po 30 września (art. 168).
- **R15.** Saldo: limit + zaległy − wykorzystany (suma godzin `UW` i `UZ` w roku) — w godzinach i dniach
  (godziny ÷ 8). Kadry widzą saldo w kartotece, Pracownik w „Moich dokumentach” (tylko odczyt, decyzja projektowa).

### Ewidencja godzin
- **R16.** Nadgodziny umowy o pracę: dzień, godziny (0,5–16), rodzaj `50`/`100` (KP art. 151¹ § 1 — 100% w nocy,
  w niedziele i święta niebędące dniami pracy, w dniu wolnym za pracę w niedzielę lub święto; 50% w pozostałe dni).
  Rodzaj wybierają Kadry — system nie zna godzin pracy w dobie (decyzja projektowa). Ostrzeżenie po przekroczeniu
  150 h w roku (art. 151 § 3 — limit można zmienić układem, regulaminem lub umową, więc nie blokuje).
- **R17.** Godziny zlecenia (stawka godzinowa): okres (zwykle miesiąc), liczba godzin, data i sposób potwierdzenia
  przez zleceniobiorcę (ustawa o minimalnym wynagrodzeniu art. 8b; forma pisemna, elektroniczna albo dokumentowa).
  Bez potwierdzenia wpis jest zapisany, ale lista płac go nie weźmie (ostrzeżenie). Okres wpisów jednej umowy się
  nie nakłada.
- **R18.** Ewidencja (nieobecności, nadgodziny) jest częścią dokumentacji pracowniczej i znika tylko z kartoteką
  (okres przechowywania z 004 R17; potwierdzenia godzin zlecenia — 3 lata, art. 8c, krótsze od okresu kartoteki).

### Audyt i role
- **R19.** Każda zmiana kalendarza, nieobecności, limitu i ewidencji jest w audycie zmian. Role: zapis — Kadry
  i Administrator; słownik świąt — Administrator; Pracownik — tylko swoje nieobecności i saldo urlopu.
- **R20.** W kontekście `dev`: kalendarze 2026 dla spółek deweloperskich z wyznaczonymi dniami za 15.08 i 26.12,
  „Anna Testowa” z limitem 2026 (26 dni) i urlopem 3 dni oraz chorobą 5 dni.

## Przypadki brzegowe
- Listopad 2026, pełny etat: 20 dni roboczych, 160 h (11.11 w środę; 1.11 w niedzielę nie obniża).
- Grudzień 2026: 24, 25 i 26.12 — sobota 26.12 obniża wymiar; bez wyznaczonego dnia wolnego wymiar = 160 h + 8 h.
- Etat 1/2: urlop 26 dni → 13 dni = 104 h; etat 3/4 przy 20 dniach → 15 dni = 120 h.
- Umowa od 15.03 do końca roku: 10 miesięcy (marzec w górę) → 10/12 × 26 = 21,67 → 22 dni.
- Choroba 30.11–2.12: 3 dni kalendarzowe, w tym dwa w grudniu — lista płac dzieli po miesiącach.

## Poza zakresem 007
- Wycena nieobecności i nadgodzin, limity 33/14 dni chorobowego, okres wyczekiwania, wybór płatnika zasiłku (art. 61
  ustawy zasiłkowej) — 009.
- Systemy czasu pracy inne niż podstawowy, harmonogramy, godziny rozpoczęcia i zakończenia pracy, pora nocna.
- Urlopy rodzicielskie, macierzyński, ojcowski, wychowawczy; zasiłek opiekuńczy.
- Zwolnienia godzinowe (`OD`, `SW` w godzinach), czas wolny za nadgodziny (art. 151²).
- Wnioski urlopowe składane przez pracownika w systemie (zgodnie z założeniem 7.2.4 — papier).
