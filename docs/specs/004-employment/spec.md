# 004 Zatrudnienie — specyfikacja

Model: [04-employment.md](../../domain/04-employment.md). Decyzje właściciela: trzy rodzaje umów (decyzja 3), historia
warunków (wymaganie 2), retencja pilnowana przez bazę (decyzja 10), zlecenie z własnym pracownikiem zablokowane
([decyzja 7.3.1](../../domain/README.md#73-decyzje-do-pytań-otwartych-2026-11-12)).

## Cel

Kadry zakładają w kartotece umowę o pracę, umowę zlecenie albo umowę o dzieło. Warunki umowy (stanowisko, etat,
wynagrodzenie, kody ZUS, ubezpieczenia, koszty uzyskania) mają wersje z datą od: zmiana warunków to nowa wersja,
a poprzednia zostaje. Z dat umów wynika status kartoteki i data, do której dokumentacji nie wolno usunąć.

## Wymagania

### Umowa
- **R1.** Kadry i Administrator zakładają umowę w kartotece: rodzaj (`employment` umowa o pracę, `mandate` zlecenie,
  `specific_work` dzieło), numer, data zawarcia, początek, koniec, a dla umowy o pracę typ (`trial` na okres próbny,
  `fixed_term` na czas określony, `indefinite` na czas nieokreślony). Razem z umową powstaje pierwsza wersja warunków
  od dnia początku. Rodzaju umowy i kartoteki nie da się zmienić.
- **R2.** Numer umowy jest unikalny w spółce (`duplicate`). Pusty numer = kolejny wolny numer liczbowy umów w spółce.
- **R3.** Daty: data zawarcia nie późniejsza niż początek (`afterStart` na `signedAt`); koniec nie wcześniejszy niż
  początek (`beforeFrom`). Koniec jest wymagany dla umowy o pracę na okres próbny i na czas określony, zlecenia
  i dzieła, a zabroniony dla umowy na czas nieokreślony (`required` / `invalid` na `endDate`).
- **R4.** Dwie umowy o pracę tej samej kartoteki nie nakładają się okresami (`overlap` na `startDate`). Okres umowy
  to od początku do dnia rozwiązania, a bez rozwiązania do końca (otwarty, gdy końca nie ma).
- **R5.** Zlecenie albo dzieło nakładające się okresem z umową o pracę tej samej kartoteki (umowa z własnym
  pracownikiem, art. 8 ust. 2a ustawy o systemie ubezpieczeń społecznych) jest odrzucane błędem `U0003` z powodem
  `concurrentTitleUnsupported` — w obie strony (także nowa umowa o pracę nachodząca na zlecenie). Decyzja 7.3.1
  mówi o zleceniu; dzieło z własnym pracodawcą przepis traktuje tak samo, więc blokada obejmuje oba rodzaje.
- **R6.** Edycja umowy (numer, typ, data zawarcia, początek, koniec) z wersją wiersza (`U0003` przy nieaktualnej).
  Zmiana początku przesuwa początek pierwszej wersji warunków; wersja zaczynająca się po nowym końcu okresu →
  `termsOutsidePeriod` na `endDate`.
- **R7.** Rozwiązanie umowy datą (nie wcześniej niż początek, nie później niż koniec umowy terminowej, nie przed
  początkiem ostatniej wersji warunków). Rozwiązanie można cofnąć (pusta data).
- **R8.** Status umowy wyliczany na dziś: `future` (przed początkiem), `active`, `ended` (po rozwiązaniu albo końcu).
- **R9.** Umowę można usunąć (pomyłka). Od modułu 009 usunięcie umowy użytej na liście płac będzie blokowane.

### Warunki umowy (wersje)
- **R10.** Wersja warunków: data od, stanowisko, etat (licznik/mianownik, 0 < licznik ≤ mianownik), podstawa
  wynagrodzenia, kwota (PLN, > 0), kody ZUS (tytuł, prawo do emerytury lub renty, niepełnosprawność), ubezpieczenia,
  koszty uzyskania przychodu. Zależnie od rodzaju umowy:

  | | umowa o pracę | zlecenie | dzieło |
  |---|---|---|---|
  | stanowisko, etat | wymagane | brak | brak |
  | podstawa (`payBasis`) | `monthly` | `hourly` albo `lumpSum` | `lumpSum` |
  | kody ZUS | wymagane | wymagane | brak |
  | koszty (`costKind`) | `standard` albo `increased` | `flat20` albo `author50` | `flat20` albo `author50` |

  Pole niedozwolone dla rodzaju → `invalid`; brakujące wymagane → `required`.
- **R11.** Kody ZUS muszą istnieć w słowniku i nie wygasnąć przed datą od wersji (`notFound`). Słownik zawiera kody
  z załącznika z 2026 r., dlatego dla wcześniejszych dat sprawdzane jest tylko to, czy kod istnieje i nie wygasł.
- **R12.** Ubezpieczenia (`pension`, `disability`, `sickness`, `accident`, `health`, `fp`, `fgsp`):
  - umowa o pracę: emerytalne, rentowe, chorobowe, wypadkowe i zdrowotne są obowiązkowe (ustawa o systemie
    ubezpieczeń społecznych art. 6 ust. 1 pkt 1, art. 11 ust. 1, art. 12 ust. 1; ustawa o świadczeniach opieki
    zdrowotnej art. 66 ust. 1 pkt 1) — brak któregoś → `missingMandatory`;
  - zlecenie: zbiór wybierają Kadry, bo zależy od faktów spoza systemu (inny tytuł u innego płatnika, status studenta
    do 26 lat, prawo do emerytury). Baza pilnuje tylko spójności: emerytalne i rentowe razem, chorobowe, wypadkowe,
    FP i FGŚP tylko przy emerytalnym (`invalid`). Front podpowiada zestawy: „jedyny tytuł”, „zbieg — tylko
    zdrowotne”, „student do 26 lat — brak”;
  - dzieło: zbiór pusty (z R5 wynika, że nie jest zawierane z własnym pracownikiem).
  FP i FGŚP przy umowie o pracę wybierają Kadry (zależą od wieku i wysokości wynagrodzenia); naliczenie — moduł 009.
- **R13.** Płaca minimalna: wynagrodzenie miesięczne umowy o pracę nie niższe niż `placa.minimalna` × etat, a stawka
  godzinowa zlecenia nie niższa niż `placa.minimalna.godzinowa` w dniu początku wersji (`belowMinimum` na
  `baseAmount`). Gdy parametru na ten dzień nie ma (umowy sprzed okresu wprowadzonych parametrów), sprawdzenia nie
  ma.
- **R14.** Nowa wersja od dnia zamyka poprzednią dzień wcześniej. Data od musi mieścić się w okresie umowy
  (`outsidePeriod`) i nie może być wcześniejsza niż początek ostatniej wersji (`beforeCurrent`); ta sama data
  poprawia ostatnią wersję. Ostatnią wersję (gdy nie jest jedyna) można usunąć — poprzednia znów jest otwarta.
- **R15.** Wersje jednej umowy nie nakładają się i pokrywają okres umowy od początku bez luk.

### Kartoteka
- **R16.** Status kartoteki na dziś: `active` (jest trwająca umowa), `future` (są tylko przyszłe), `former` (wszystkie
  zakończone), `none` (brak umów). Lista kartotek pokazuje status i filtruje po nim.
- **R17.** Okres przechowywania dokumentacji liczony z umów (`retentionUntil` na kartotece):
  - umowa o pracę zawarta od 1.01.2019: 10 lat od końca roku kalendarzowego, w którym stosunek pracy ustał (Kodeks
    pracy art. 94 pkt 9b; ustawa z 10.01.2018, Dz.U. 2018 poz. 357, art. 13);
  - umowa o pracę zawarta przed 1.01.2019: 50 lat od dnia zakończenia pracy (ustawa z 10.01.2018 art. 7 ust. 2;
    ustawa o emeryturach i rentach z FUS art. 125a ust. 4). Skrócenie po złożeniu raportu informacyjnego ZUS RIA
    (art. 7 ust. 3) nie jest obsługiwane — system liczy dłuższy okres;
  - zlecenie: jak umowa o pracę — 10 lat od końca roku dla ubezpieczonych zgłoszonych po 31.12.2018, inaczej 50 lat
    od zakończenia pracy (ustawa o emeryturach i rentach z FUS art. 125a ust. 4, 4a i 4b; zgłoszenie = początek
    umowy);
  - dzieło: *założenie zachowawcze* — jak zlecenie zawarte od 2019 r. (10 lat od końca roku). Okres dla dokumentacji
    wyłącznie podatkowej do potwierdzenia ze źródłem w module 010;
  - umowa bez daty zakończenia → okresu nie da się policzyć, kartoteki nie wolno usunąć.
  Przedłużenia z art. 94⁴ Kodeksu pracy (spór sądowy) i praca górnicza (art. 5 ustawy z 2018 r.) — poza zakresem.
- **R18.** Kartotekę z umowami można usunąć dopiero po upływie okresu przechowywania wszystkich umów; inaczej
  `U0003` z powodem `retention`. Kartoteka bez umów — jak w 003. Usunięcie usuwa też umowy, a audyt zostaje bez
  danych osobowych.

### Audyt i role
- **R19.** Każda zmiana umowy i wersji warunków jest w audycie zmian. Odczyt szczegółów umowy (warunki z
  wynagrodzeniem) zapisuje audyt odczytów jak kartoteka (003 R9).
- **R20.** Pracownik nie ma dostępu do umów (`U0004`); zobaczy je w module dokumentów (010).

### Dane deweloperskie
- **R21.** W kontekście `dev` kartoteka „Anna Testowa” w spółce „Demo” ma umowę o pracę na czas nieokreślony od
  2026-01-01, pełny etat, 6 000,00 zł, kody 0110/0/0.

## Przypadki brzegowe
- Umowa o pracę kończy się 31 grudnia, następna zaczyna 1 stycznia — nie nakładają się.
- Zlecenie po rozwiązaniu umowy o pracę (dzień po dniu rozwiązania) — dozwolone.
- Rozwiązanie umowy, w której ostatnia wersja zaczyna się później → `beforeCurrent` na `terminatedAt`; najpierw
  usuń wersję.
- Etat 1/2 przy płacy minimalnej 4 806 zł → minimum 2 403,00 zł; porównanie bez zaokrągleń (`kwota × mianownik ≥ płaca × licznik`).
- Umowa o pracę zakończona 15.03.2026 → przechowywanie do 31.12.2036.

## Poza zakresem 004
- Oskładkowanie zlecenia i dzieła z własnym pracownikiem (decyzja 7.3.1 — osobna specyfikacja).
- Zgodność kodu tytułu z rodzajem umowy (np. 0110 tylko dla umowy o pracę) — Kadry wybierają kod; ewentualna reguła
  przy deklaracjach ZUS (poza zakresem produktu).
- Raport ZUS RIA i skracanie okresu przechowywania umów sprzed 2019 r.
- Sposób rozwiązania umowy, świadectwo pracy, aneksy jako dokumenty, wymiar urlopu (007).
- Dodatki stałe do wynagrodzenia (006) i rozliczenie godzin zlecenia (007).
