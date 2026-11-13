# 008 PPK — specyfikacja

Model: [08-ppk.md](../../domain/08-ppk.md). Decyzja właściciela 5: uczestnictwo ze stawkami wpłat, deklaracje
(rezygnacja, ponowny zapis, wpłaty dodatkowe, obniżona wpłata), automatyczny zapis co 4 lata; wpłaty liczy lista płac
(009); bez wymiany plików z instytucją finansową. Właściciel 2026-11-13: „rób bez pytania” — specyfikacja przyjęta
bez osobnego zatwierdzenia, decyzje projektowe oznaczone poniżej.

Podstawa prawna (stan na 13.11.2026): ustawa o pracowniczych planach kapitałowych, t.j. Dz.U. 2026 poz. 192 ze zm.
Dz.U. 2026 poz. 989 („PPK”); Kodeks pracy, t.j. Dz.U. 2026 poz. 1245; ustawa o PIT, t.j. Dz.U. 2026 poz. 592.

## Cel

Kadry prowadzą PPK spółki: umowę o zarządzanie z instytucją finansową i wpłatę dodatkową pracodawcy, uczestnictwo
osób zatrudnionych ze stawkami, deklaracje i wnioski. System wskazuje, kogo i do kiedy trzeba zapisać, kto wymaga
wniosku (55–70 lat), a kogo wznowić przy zapisie co 4 lata. **Kwot wpłat tu nie ma** — liczy je 009 ze stawek
ważnych w miesiącu wypłaty.

## Wymagania

### PPK spółki
- **R1.** Ustawienia PPK spółki z okresem obowiązywania: instytucja finansowa (nazwa), numer umowy o zarządzanie,
  data jej zawarcia, wpłata dodatkowa pracodawcy 0–2,5% (art. 26 ust. 2; zmiana od miesiąca następującego po zmianie
  umowy — art. 26 ust. 4, więc `validFrom` = pierwszy dzień miesiąca). Różnicowanie wpłaty dodatkowej na grupy
  (art. 26 ust. 3) — poza zakresem. Okresy się nie nakładają.

### Osoba zatrudniona i termin zapisu
- **R2.** Kartoteka jest „osobą zatrudnioną” (art. 2 ust. 1 pkt 18) w okresach umów: umowa o pracę (bez młodocianych
  — art. 190 KP: osoba poniżej 18 lat), zlecenie, gdy warunki mają ubezpieczenie emerytalne i rentowe (obowiązkowe;
  dobrowolności system nie rozróżnia — decyzja: zestaw ubezpieczeń z warunków). Dzieło — nie.
- **R3.** Okres zatrudnienia do zapisu: 3 miesiące (art. 16 ust. 1), z okresów zatrudnienia w tej spółce w 12
  miesiącach wstecz (art. 16 ust. 2; różne tytuły sumowane — interpretacja). Termin zawarcia umowy o prowadzenie PPK:
  10. dzień miesiąca następującego po miesiącu, w którym upłynęły 3 miesiące; najwcześniej po 14 dniach zatrudnienia.
- **R4.** Wiek (art. 15): 18–54 — zapis automatyczny; 55–69 — tylko na wniosek osoby (pracodawca informuje);
  70+ w pierwszym dniu zatrudnienia — bez umowy. Wiek z daty urodzenia kartoteki.
- **R5.** Lista „do zapisu” spółki na dzień: osoby zatrudnione, które osiągnęły 3 miesiące, nie mają uczestnictwa
  ani rezygnacji, z terminem i statusem: `toEnroll` (18–54), `requestOnly` (55–69 — czeka na wniosek), `overdue`
  (termin minął — umowa powstała z mocy prawa, art. 17). Osoby bez 3 miesięcy z datą, kiedy je osiągną.

### Uczestnictwo
- **R6.** Uczestnictwo kartoteki: data zawarcia umowy o prowadzenie PPK (`enrolledAt`), numer w instytucji
  (opcjonalnie), stawki z okresem: podstawowa pracownika 2% albo obniżona 0,5–2% (art. 27 ust. 2), dodatkowa
  pracownika 0–2% (art. 27 ust. 3); stawki pracodawcy z ustawień spółki (1,5% + dodatkowa — R1). Okresy stawek się
  nie nakładają; nowy okres zamyka poprzedni dzień wcześniej.
- **R6a.** Uczestnictwo wpisane przez pomyłkę Kadry usuwają (z wersją wiersza) razem z okresami stawek; deklaracje
  kartoteki zostają (od 009 — nie, gdy uczestnictwo użyto na liście płac).
- **R7.** Zapis osoby 55–69 lat wymaga wniosku (deklaracja `enrollmentRequest`) z datą wcześniejszą albo równą
  dacie zapisu (`requestMissing`). 70+ → `tooOld`.
- **R8.** Wpłaty są należne od wynagrodzenia wypłaconego po zawarciu umowy (art. 28 ust. 1) — 009 bierze stawki
  ważne w dniu wypłaty.

### Deklaracje i wnioski (niezmienne, z datą złożenia; forma pisemna — art. 23 ust. 2, 10)
- **R9.** Rodzaje i skutek:

  | `kind` | Co | Skutek | Podstawa |
  |---|---|---|---|
  | `resignation` | deklaracja rezygnacji z wpłat | brak wpłat **od miesiąca złożenia** (pobrane w tym miesiącu — zwrot przez 009) | art. 23 ust. 2, 4 |
  | `restartRequest` | wniosek o dokonywanie wpłat po rezygnacji | wpłaty od miesiąca następującego po złożeniu | art. 23 ust. 10–11 |
  | `enrollmentRequest` | wniosek osoby 55–69 o zawarcie umowy | umożliwia zapis (R7) | art. 15 ust. 2 |
  | `reducedRate` | deklaracja obniżenia wpłaty podstawowej (0,5–2%) | od miesiąca następującego; 009 pomija obniżenie w miesiącu, gdy wynagrodzenie u spółki > 1,2 × minimalne (art. 27 ust. 5) | art. 27 ust. 2, 4, 7 |
  | `additionalRate` | deklaracja wpłaty dodatkowej pracownika (0–2%) | od miesiąca następującego | art. 27 ust. 3, 3a |

- **R10.** `reducedRate` i `additionalRate` tworzą nowy okres stawek od pierwszego dnia miesiąca po złożeniu.
  `restartRequest` po rezygnacji wznawia wpłaty (nowy okres stawek z ostatnimi stawkami). Rezygnacja przed zapisem
  (art. 16 ust. 1) zdejmuje osobę z listy „do zapisu”.
- **R11.** Status uczestnika na dzień: `active`, `resigned` (od miesiąca rezygnacji do miesiąca przed wznowieniem),
  `notEnrolled`. Funkcja dla 009: stawki obowiązujące w dniu (z uwzględnieniem rezygnacji), wartość 0, gdy wpłat nie
  ma.

### Ponowny zapis co 4 lata
- **R12.** Cykl z art. 23 ust. 5–6: informacja do ostatniego dnia lutego, wznowienie wpłat od 1 kwietnia; pierwszy
  cykl 2023, następny **2027** (art. 23 ust. 5 „co 4 lata”; mojeppk.pl: informacja do końca lutego 2027, wpłaty od
  1.04.2027). Daty cyklu jako parametry prawne
  (`ppk.ponowny.zapis.informacja`, `ppk.ponowny.zapis.wznowienie`).
- **R13.** Lista „ponowny zapis” spółki na rok cyklu: osoby z rezygnacją, bez wznowienia, z podziałem: wznowienie
  od 1.04 (do 55 lat), tylko na wniosek do końca lutego (55–69, ust. 9), bez wznowienia (70+, ust. 8). Kadry
  zaznaczają przekazanie informacji (data). Wznowienie wykonuje rutyna dla całej listy: rezygnacja kończy się 31.03,
  nowy okres stawek od 1.04 — chyba że osoba złożyła nową rezygnację po informacji.

### Samoobsługa, audyt, role
- **R14.** Pracownik widzi w „Moich dokumentach” swój status PPK, stawki i deklaracje (tylko odczyt).
- **R15.** Każda zmiana w audycie zmian. Zapis — Kadry i Administrator.
- **R16.** Dane deweloperskie: PPK spółki Demo (fikcyjna instytucja, wpłata dodatkowa pracodawcy 0%), Anna Testowa —
  uczestnik od 2026-04-10 ze stawkami 2% / 0%; jedna osoba z rezygnacją.

## Przypadki brzegowe
- Zatrudnienie od 2026-01-15 → 3 miesiące 2026-04-14 → termin zapisu 2026-05-10.
- Zlecenie bez ubezpieczenia emerytalnego (student) → nie jest osobą zatrudnioną.
- Rezygnacja złożona 2026-06-20 → brak wpłat od czerwca 2026; wniosek o wznowienie 2026-09-03 → wpłaty od października.
- Osoba urodzona 1972-02-10 (55 lat w 2027) z rezygnacją → przy cyklu 2027 tylko na wniosek do 28.02.2027.

## Poza zakresem 008
- Wyliczanie i przekazywanie wpłat, zwroty, PIT od wpłaty pracodawcy, składki — 009.
- Pliki i komunikacja z instytucją finansową, transfery (art. 19), wpłata powitalna i dopłaty roczne (PFR).
- Różnicowanie wpłaty dodatkowej pracodawcy na grupy, przestój i niewypłacalność (art. 25 ust. 4–5).
- Okres przechowywania dokumentacji PPK (ustawa go nie określa; dokumenty zostają z kartoteką — 004 R17).
