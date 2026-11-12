# 002 Słowniki — specyfikacja

Model: [02-dictionaries.md](../../domain/02-dictionaries.md). Decyzje właściciela: kody ZUS tylko jako słownik
i przypisanie (decyzja 7), stanowiska wspólne dla grupy (model domeny).

## Cel

Kadry wybierają w zatrudnieniu stanowisko i kody ZUS ze słownika, a nie wpisują ich ręcznie. Spółka ma urząd
skarbowy płatnika potrzebny w PIT-4R i PIT-8AR, a pracownik (moduł 003) urząd, do którego idzie PIT-11. Kody ZUS
i urzędy pochodzą z oficjalnych wykazów i są w bazie od pierwszej migracji.

## Wymagania

### Stanowiska
- **R1.** Kadry i Administrator edytują stanowisko: nazwa (unikalna bez względu na wielkość liter), opis, aktywność.
  Edycja wymaga wersji wiersza; nieaktualna wersja → `U0003` (409).
- **R2.** Lista stanowisk domyślnie pokazuje tylko aktywne; przełącznik „pokaż nieaktywne”. Stanowiska się nie usuwa.

### Kody ZUS
- **R3.** Słownik ma trzy rodzaje kodów: tytuł ubezpieczenia (4 cyfry), prawo do emerytury lub renty (1 cyfra),
  stopień niepełnosprawności (1 cyfra). Kod jest unikalny w obrębie rodzaju. Każdy ma nazwę z wykazu i okres
  obowiązywania (`od`, opcjonalnie `do`).
- **R4.** Baza zawiera kody z oficjalnego wykazu (dane, nie dane deweloperskie), ze źródłem w changesecie.
- **R5.** Kadry i Administrator widzą listę z filtrem rodzaju, wyszukiwaniem po kodzie i nazwie oraz filtrem
  „obowiązujące w dniu”.
- **R6.** Administrator dodaje kod (rodzaj, kod, nazwa, od, do) i edytuje nazwę oraz daty. Rodzaju i kodu nie da się
  zmienić. `do` wcześniejsze niż `od` → `U0001`. Kod w złym formacie dla rodzaju → `U0001`.
- **R7.** Funkcja wewnętrzna sprawdza, czy kod danego rodzaju obowiązuje w dniu. Użyje jej moduł 004 przy
  przypisaniu kodów do zatrudnienia.

### Urzędy skarbowe
- **R8.** Słownik urzędów: kod (4 cyfry, unikalny), nazwa, aktywność. Baza zawiera pełny wykaz z oficjalnego
  schematu e-Deklaracji, ze źródłem w changesecie.
- **R9.** Kadry i Administrator widzą listę z wyszukiwaniem po kodzie i nazwie. Administrator dodaje urząd i edytuje
  nazwę oraz aktywność (kod jest niezmienny). Edycja wymaga wersji wiersza.
- **R10.** Spółka ma urząd skarbowy płatnika. Pole jest wymagane przy zakładaniu i edycji spółki; spółki założone
  przed modułem 002 mają je puste do pierwszej edycji. Nieaktywnego urzędu nie można wybrać (`U0001`, kod
  `inactive`), ale spółka, która już go ma, zachowuje go przy edycji innych pól.

### Wspólne
- **R11.** Każdy zapis zostawia wpis audytu (stan przed i po). Uprawnienia sprawdza baza: brak roli → `U0004`.
- **R12.** Menu „Słowniki” (Kadry, Administrator): Stanowiska, Kody ZUS, Urzędy skarbowe. Przyciski dodawania
  i edycji kodów ZUS oraz urzędów widzi tylko Administrator.

## Źródła danych
- Kody ZUS: załącznik nr 24 „Kody wykorzystywane przy wypełnianiu dokumentów” do rozporządzenia MRPiPS z 9 kwietnia
  2026 r. (Dz.U. 2026 poz. 518, w mocy od 30 kwietnia 2026 r.): 118 kodów tytułu, 3 kody prawa do emerytury lub
  renty, 5 kodów niepełnosprawności (w tym `4` dla osób do 16. roku życia). `valid_from` = 2026-04-30.
- Urzędy: `KodyUrzedowSkarbowych_v8-0E.xsd` (crd.gov.pl, przestrzeń 2022/01/05), 400 urzędów.

## Przypadki brzegowe
- Ten sam kod tytułu z nową nazwą od nowej daty: na razie edycja nazwy (słownik nie trzyma historii nazw).
  Historię zmian zapisuje audyt.
- Kod wycofany (`do` w przeszłości) zostaje w słowniku; nowe zatrudnienie go nie wybierze (R7), stare dalej go
  pokazuje.
- Wyszukiwanie urzędu po fragmencie kodu (`14`) i nazwy (`Śródmieście`), bez względu na wielkość liter.

## Poza zakresem 002
- Obowiązkowe ubezpieczenia dla tytułu (emerytalne, rentowe, chorobowe, wypadkowe, zdrowotne): rozstrzyga reguła
  zbiegu tytułów w module 004 na podstawie umowy, nie słownik. Atrybut `insurances` z modelu przechodzi do 004.
- Urząd skarbowy pracownika — pole kartoteki w module 003.
- Import wykazów z pliku przez UI. Nowa wersja wykazu przychodzi jako changeset danych.
- Usuwanie pozycji słowników.
- Węższy wykaz urzędów dla PIT-11 (`KodyUrzedowSkarbowychExWUS_v8-0E`, bez urzędów wyspecjalizowanych) — moduł 010.
