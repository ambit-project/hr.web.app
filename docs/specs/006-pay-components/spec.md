# 006 Składniki płacowe, zgody na potrącenia, zajęcia wynagrodzenia — specyfikacja

Model: [06-pay-components.md](../../domain/06-pay-components.md). Decyzje właściciela: definicje dodatków i potrąceń
z właściwościami prawnymi i przypisanie do zatrudnienia (wymaganie 6), rejestr zgód powiązany z potrąceniem
(wymaganie 10), oświadczenia i dokumenty na papierze rejestrowane w systemie
([założenie 7.2.4](../../domain/README.md#72-założenia-modelu)).

Podstawa prawna: Kodeks pracy, tekst jednolity Dz.U. 2026 poz. 1245 (dalej „KP”); Kodeks postępowania cywilnego,
Dz.U. 2026 poz. 468 (późniejsze zmiany do 13.11.2026 nie dotyczą przepisów poniżej, dalej „KPC”); Kodeks cywilny,
Dz.U. 2026 poz. 795 („KC”). Wzorzec funkcjonalny: grupy potrąceń i zajęcia wynagrodzeń w systemach kadrowo-płacowych
(limit alimentów 60% bez kwoty wolnej, innych tytułów 50% z kwotą wolną proporcjonalną do etatu).

## Cel

Administrator prowadzi słownik składników: dodatków (zwiększają przychód) i potrąceń (zmniejszają wypłatę) z cechami,
od których zależy naliczenie. Kadry przypisują składniki do umów na okres, rejestrują pisemne zgody pracownika na
potrącenia i zajęcia wynagrodzenia (alimenty, inne egzekucje). **Liczenie** potrąceń — kolejność, limity i kwoty wolne
— robi lista płac (009); ten moduł daje jej dane i pilnuje, żeby były spójne.

## Przepisy, które moduł odwzorowuje (liczy 009)

| Reguła | Podstawa |
|---|---|
| Potrąca się z wynagrodzenia po odliczeniu składek na ubezpieczenia społeczne, zaliczki PIT i wpłat PPK; bez zgody tylko: (1) alimenty z tytułu wykonawczego, (2) inne należności z tytułu wykonawczego, (3) zaliczki pieniężne, (4) kary porządkowe — w tej kolejności | KP art. 87 §1–2 |
| Limity: alimenty do 3/5, inne egzekucje i zaliczki do 1/2 (razem z alimentami do 3/5); kary osobno: 1/10 wynagrodzenia po potrąceniach (1)–(3) | KP art. 87 §3–4, art. 108 §3 |
| Kwota wolna (minimalne wynagrodzenie po składkach, PIT i PPK, proporcjonalnie do etatu): inne egzekucje 100%, zaliczki 75%, kary 90%; alimenty — bez kwoty wolnej | KP art. 87¹ §1–2 |
| Inne potrącenia tylko za zgodą wyrażoną na piśmie; kwota wolna 100% (na rzecz pracodawcy) albo 80% (na rzecz innych) | KP art. 91 §1–2 |
| Alimenty bez egzekucji — na wniosek wierzyciela z tytułem wykonawczym | KP art. 88 |
| Zajęcie obejmuje wynagrodzenie za prace zlecone; do świadczeń powtarzających się, które zapewniają utrzymanie, stosuje się art. 87 i 87¹ KP | KPC art. 881 §2, art. 833 §2¹ |
| Zajęcie trwa przy nowym stosunku pracy lub zlecenia u tego pracodawcy | KPC art. 884 §1 |
| Zajęcie jest dokonane z chwilą doręczenia wezwania pracodawcy; przy zbiegu egzekucji sądowej i administracyjnej płaci się organowi, który zajął pierwszy | KPC art. 883 §1, art. 882¹ §1 |

## Wymagania

### Słownik składników
- **R1.** Składnik: kod (unikalny, 1–20 znaków: wielkie litery, cyfry, `-`; po założeniu niezmienny), nazwa, rodzaj
  (`addition` dodatek, `deduction` potrącenie), sposób liczenia, aktywność. Kod w dowolnej wielkości liter jest
  zapisywany wielkimi literami; zajęty → `duplicate`.
- **R2.** Sposób liczenia (`calculation`): `fixed` kwota stała, `percentOfBase` procent wynagrodzenia zasadniczego
  z warunków umowy, `rateTimesHours` stawka × godziny (godziny z modułu 007), `engine` liczy lista płac. Potrącenie
  ma tylko `fixed`; `engine` mają tylko składniki systemowe (`invalid`).
- **R3.** Dodatek ma cechy: `inZusBase` (podstawa składek społecznych), `inHealthBase` (podstawa zdrowotnej),
  `taxable` (przychód do PIT), `inSicknessBase` (podstawa wynagrodzenia chorobowego). Potrącenie ich nie ma
  (`invalid`). System nie ocenia, czy cechy są zgodne z przepisami dla danego świadczenia — ustawia je Administrator,
  a zmiana cech trafia do audytu.
- **R4.** Potrącenie ma kategorię (`deductionCategory`, wymagana): `advance` zaliczka pieniężna (art. 87 §1 pkt 3),
  `penalty` kara porządkowa (art. 108), `voluntaryEmployer` dobrowolne na rzecz pracodawcy, `voluntaryOther`
  dobrowolne na rzecz innych podmiotów (art. 91 §2 — inna kwota wolna). Alimenty i inne egzekucje nie są kategoriami
  do wyboru: potrącenia z zajęć (R16) liczy lista płac jako składniki systemowe. Potrącenia dobrowolne wymagają zgody
  (`requiresConsent`, wyliczane).
- **R5.** Składniki systemowe (`isSystem`) są zakładane migracją i tylko do odczytu: `ZASAD` wynagrodzenie zasadnicze,
  `CHOR` wynagrodzenie chorobowe, `NADG50` i `NADG100` nadgodziny z dodatkiem 50% i 100%, `ZLEC` wynagrodzenie ze
  zlecenia, `DZIELO` wynagrodzenie z umowy o dzieło, `ALIM` potrącenie alimentacyjne, `KOMOR` potrącenie z innego
  tytułu wykonawczego. Ich cechy podaje migracja z komentarzem źródła; potwierdzenie z przepisami — przy 009.
- **R6.** Administrator zakłada i zmienia składniki (nazwa, cechy, kategoria, sposób liczenia, aktywność; z wersją
  wiersza). Zmiana rodzaju albo sposobu liczenia składnika przypisanego do umowy → `U0003` z powodem `componentInUse`.
  Nieaktywnego nie da się przypisać. Kadry i Administrator czytają słownik.

### Składniki umowy
- **R7.** Kadry przypisują do umowy składnik (aktywny, niesystemowy) z wartością i okresem: `validFrom`, `validTo`
  (puste = bez końca). Wartość > 0: dla `fixed` kwota w zł (2 miejsca), dla `percentOfBase` procent (do 1000, 2
  miejsca), dla `rateTimesHours` stawka za godzinę w zł.
- **R8.** Okres mieści się w okresie umowy (`outsidePeriod`); pusty koniec składnika oznacza „do końca umowy”; koniec nie przed początkiem (`beforeFrom`). Ten sam
  składnik na tej samej umowie nie nakłada się okresami (`overlap`).
- **R9.** Kara porządkowa tylko na umowie o pracę (art. 108 dotyczy pracowników) → `invalid` na `componentTypeId`.
- **R10.** Potrącenie dobrowolne wymaga wskazania zgody (`consentId`) tej samej kartoteki i tego samego składnika,
  podpisanej, obejmującej cały okres składnika (zgoda bez końca obejmuje składnik bez końca) → inaczej
  `consentMissing` na `consentId`. Wartość nie większa niż kwota zgody → inaczej `aboveConsent` na `value`. Zgoda
  jest wymagana przy każdym rodzaju umowy: art. 91 KP dotyczy pracownika, a przy zleceniu i dziele potrącenie wynika
  z umowy stron — pisemna zgoda pokrywa oba przypadki (decyzja projektowa).
- **R11.** Treść przypisania jest niezmienna; zmiana wartości = zakończenie (ustawienie `validTo`) i nowe przypisanie.
  Zakończenie i cofnięcie zakończenia z wersją wiersza; cofnięcie podlega R8 i R10. Przypisanie można usunąć
  (pomyłka); od modułu 009 usunięcie przypisania użytego na liście płac będzie blokowane.
- **R12.** Lista składników umowy na stronie umowy: składnik, wartość, okres, zgoda, status (`future`, `active`,
  `ended`). Odczyt zapisuje audyt odczytów jak warunki umowy (004 R19), bo zawiera kwoty.

### Zgody na potrącenia
- **R13.** Zgoda: kartoteka, składnik wymagający zgody, kwota miesięczna (> 0), `validFrom`, `validTo` (puste = bez
  końca), data podpisania (`signedAt`, puste = dokument przygotowany, niepodpisany — zgoda wtedy nie działa).
  Podpisanie nie później niż dziś (`future`). Status: `unsigned`, `future`, `active`, `ended`.
- **R14.** Zgoda „na piśmie” (art. 91 §1): `signedAt` zaznaczają Kadry po otrzymaniu dokumentu podpisanego
  własnoręcznie albo kwalifikowanym podpisem elektronicznym (KC art. 78¹ — równoważny formie pisemnej). Kliknięcie
  w aplikacji ani e-mail nie są zgodą — dlatego pracownik nie wyraża zgody w systemie.
- **R15.** Niepodpisaną zgodę można zmieniać i usuwać. Podpisanej treść jest niezmienna; można ją zakończyć datą
  (odwołanie przez pracownika albo koniec umowy) i cofnąć zakończenie. Zakończenie przed końcem przypisanego składnika
  → `U0003` z powodem `consentInUse` (najpierw zakończ składnik). Usunięcie zgody wskazanej w składniku → `U0003`
  `consentInUse`. Odwołanie zgody na przyszłość: KP go nie reguluje — system pozwala zakończyć zgodę datą; skutki
  prawne odwołania do potwierdzenia przed 009.
- **R16.** Pracownik widzi swoje zgody w „Moich dokumentach” (składnik, kwota, okres, status) — tylko odczyt.

### Zajęcia wynagrodzenia
- **R17.** Zajęcie: kartoteka, rodzaj (`alimony` alimenty, `other` inna należność), organ (`court` komornik sądowy,
  `administrative` organ egzekucji administracyjnej, `creditor` wierzyciel alimentacyjny z tytułem wykonawczym —
  art. 88 KP, tylko dla alimentów → inaczej `invalid`), nazwa organu, sygnatura, data doręczenia (nie z przyszłości),
  kwoty: `monthlyAmount` (alimenty bieżące), `totalAmount` (należność do ściągnięcia z kosztami), uwagi. Inna należność
  wymaga `totalAmount`; alimenty wymagają co najmniej jednej z kwot (`required`).
- **R18.** Sygnatura z organem jest unikalna w kartotece (`duplicate`).
- **R19.** Status: `active`, `paidOff` (spłacone), `discontinued` (umorzone albo zwolnione z zajęcia) z datą zmiany
  (nie wcześniej niż doręczenie); powrót do `active` dozwolony. Zmiana danych zajęcia z wersją wiersza. Usunięcie
  (pomyłka) — od 009 blokowane, gdy potrącono z zajęcia na liście płac.
- **R20.** Zajęcie dotyczy kartoteki, czyli wszystkich umów w tej spółce (umowa o pracę, zlecenie — KPC art. 881 §2,
  art. 884 §1). Czy wynagrodzenie z danej umowy podlega limitom KP (art. 833 §2¹ KPC: świadczenia powtarzające się),
  ocenia lista płac (009).
- **R21.** Kwota ściągnięta (`collectedAmount`) jest wyliczana z zamkniętych list płac (009); do tego czasu 0.
  Kolejność na liście zajęć: alimenty przed innymi, potem data doręczenia (kolejność do liczenia — 009).
- **R22.** Odczyt zajęć zapisuje audyt odczytów (dane o długach są szczególnie wrażliwe); Pracownik nie ma do nich
  dostępu (`U0004`).

### Audyt, role, dane deweloperskie
- **R23.** Każda zmiana słownika, przypisania, zgody i zajęcia jest w audycie zmian.
- **R24.** Role: słownik — zapis Administrator, odczyt Kadry i Administrator; przypisania, zgody, zajęcia — Kadry
  i Administrator; Pracownik — tylko swoje zgody (R16).
- **R25.** W kontekście `dev`: składniki `PREMIA` (premia uznaniowa, dodatek, kwota stała, wszystkie cechy) i `KARTA`
  (karta sportowa, potrącenie dobrowolne na rzecz innych); „Anna Testowa” (Demo) ma podpisaną zgodę `KARTA` 120,00 zł
  od 2026-01-01 i przypisane do umowy potrącenie `KARTA` 120,00 zł od 2026-01-01.

## Przypadki brzegowe
- Zgoda 100 zł, składnik 120 zł → `aboveConsent`.
- Zgoda od 2026-02-01, składnik od 2026-01-01 → `consentMissing`.
- Zgoda do 2026-06-30, składnik bez końca → `consentMissing`; zakończ składnik 2026-06-30 albo przedłuż zgodę.
- Zakończenie zgody 2026-03-31, gdy składnik trwa do 2026-06-30 → `consentInUse`.
- Kara porządkowa przypisana do zlecenia → `invalid`.
- Alimenty od wierzyciela (art. 88) z kwotą bieżącą 800 zł i bez kwoty do ściągnięcia — poprawne; inna należność bez
  `totalAmount` → `required`.

## Poza zakresem 006
- Liczenie potrąceń, limitów, kwot wolnych i podziału między kilka zajęć; zawiadomienia komornika o zbiegu
  i przekazy — 009.
- Dokument zgody do podpisu (wydruk) — 010.
- Kasy zapomogowo-pożyczkowe (PKZP), pożyczki z zakładowego funduszu, składki związkowe — jako zwykłe składniki
  `voluntaryEmployer`/`voluntaryOther`, bez osobnej obsługi.
- Egzekucja ze świadczeń ZUS i zasiłków wypłacanych przez płatnika (inne kwoty wolne).

## Do potwierdzenia przed 009 (nie wynika wprost z przepisów)
- Odliczanie składki zdrowotnej przy ustalaniu podstawy limitów i kwoty minimalnej netto (art. 87 §1 wymienia tylko
  składki społeczne).
- Kwota wolna przy zleceniu (art. 833 §2¹ KPC: „odpowiednio”).
- Skutki odwołania zgody (R15) i zasady przy samych zajęciach administracyjnych.
- Cechy składników systemowych (R5).
