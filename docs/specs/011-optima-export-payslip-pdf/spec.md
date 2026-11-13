# 011 Eksport do Optimy i PDF paska — specyfikacja

Zlecenie właściciela (2026-11-13): eksport listy pracowników do formatu Comarch ERP Optima i PDF paska wypłaty.
Wzór rozwiązania: ambit.es (`OptimaHrImportSheet`, `OptimaWorkerRow`, `PdfDocumentBuilder`, `PdfFonts`) — ten sam
układ arkusza i te same biblioteki. Zasada projektu: czego nie wiemy, tego nie robimy — kolumny Optimy bez
potwierdzonego formatu wartości zostają puste (Optima wpisuje swoje domyślne).

Źródła: pomoc Comarch ERP Optima 2026.5 „Z arkusza MS Excel” (pomoc.comarch.pl/optima/pl/2026_5/?p=817) — import
Narzędzia / Importy / Danych kadrowych / z arkusza MS Excel, plik `*.xls`, minimum Kod + nazwisko i imię, Kod
(akronim) łączy z istniejącym pracownikiem (Pomiń / Nadpisz), daty RRRR-MM-DD, nagłówki bez spacji i polskich liter,
wzorzec = plik z „Eksportu danych kadrowych” Optimy; ambit.es — 139 nagłówków w kolejności `kadry.xls` eksportowanego
przez Optimę, arkusz `Dane_kadrowe`.

## Wymagania

### Eksport do Optimy
- **R1.** Kadry i Administrator pobierają z listy pracowników plik do importu w Optimie — dla kartotek zaznaczonych
  albo (bez zaznaczenia) wszystkich spełniających bieżące filtry listy (spółka, status, wyszukiwanie).
- **R2.** Format: **`.xls` (Excel 97-2003)**, nie `.xlsx` — pomoc Optimy podaje dla importu tylko `*.xls`
  **[D: zgodnie z ambit.es; `.xlsx` dopiero po sprawdzeniu, że Optima go przyjmuje]**. Jeden arkusz `Dane_kadrowe`,
  wiersz 1 = 139 nagłówków w kolejności Optimy, wiersz na kartotekę. Limit 65 535 wierszy (`.xls`) — więcej →
  błąd z prośbą o zawężenie filtrów.
- **R3.** Wypełniane kolumny (potwierdzone znaczenie i format): `Kod` = akronim kartoteki (≤ 20 znaków — akronim
  003 ma 14), `Nazwisko`, `Imie`, `Drugie_imie`, `Data_urodzenia`, `Plec` (`K`/`M` z PESEL, 10. cyfra parzysta = K;
  bez PESEL puste), `Pesel`, `Archiwalny` (`1` dla byłego pracownika — status `former`, `0` w innym razie), bieżący
  adres zamieszkania w bloku `_zamieszkania` i korespondencyjny w `_korespondencja` (`Kraj` nazwą, `Kod_kraju`,
  `Miejscowosc`, `Ulica`, `Nr_domu`, `Nr_lokalu`, `Kod_pocztowy`; ulica rozdzielona jak w PIT-11), `Email`. Blok
  zameldowania (bez przyrostka) zostaje z domyślnymi Optimy — kartoteka nie zna adresu zameldowania. Każda komórka jako
  tekst (PESEL zachowuje zero na początku), poza kolumnami liczbowymi Optimy z wartością domyślną.
- **R4.** Niewypełniane **[?]** (format wartości w imporcie niepotwierdzony): dane etatowe (`Rodzaj_umowy`,
  `Rodzaj_zatrudnienia`, `Stanowisko`, wymiar, stawka, daty zatrudnienia), ubezpieczenia (`Kod_tytulu_ubezpieczenia`,
  `Emerytalne`…), urząd skarbowy (`US_*`), PPK, rachunek bankowy (forma płatności), dokument tożsamości. Kolumny są
  w arkuszu z domyślną Optimy albo puste. Rozszerzenie — po sprawdzeniu na wzorcowym `kadry.xls` z Optimy.
- **R5.** Pobranie zapisuje audyt odczytu (`optima_export`, liczba kartotek, filtry) — plik zawiera PESEL. Odpowiedź
  z `Cache-Control: no-store`. Nazwa `optima-kadry-RRRR-MM-DD.xls`. Bez wybranej spółki
  eksport jest niedostępny (`companyId` `required`) — import w Optimie jest do bazy jednej firmy.

### PDF paska wypłaty
- **R6.** Pasek wypłaty z zamkniętej listy (010 R1) do pobrania jako PDF: te same dane co strona do wydruku
  (nagłówek: pracodawca, pracownik, umowa, lista, okres, data wypłaty; pozycje według stron z podstawą, stawką,
  kwotą i sumami; netto, do wypłaty). Zmienia decyzję 010 R2 („bez biblioteki PDF”) **[D: PDFsharp-MigraDoc 6.2.4
  na licencji MIT (docs.pdfsharp.net), wersja przypięta; czcionka Noto Sans (OFL) osadzona — polskie znaki]**.
- **R7.** PDF pobiera Kadry/Administrator (przy wypłacie, w kartotece „Dokumenty”) i pracownik — swój pasek
  w „Moich dokumentach”. Odczyt cudzego paska — audyt jak 010 R3 (`payslip`). Nazwa
  `pasek-<numer listy>-<data wypłaty>.pdf`, `Cache-Control: no-store`.
- **R8.** Wypłata z listy niezamkniętej — 409 `notClosed` (jak 010).

### Role
- **R9.** Eksport do Optimy — Kadry, Administrator. PDF paska — Kadry, Administrator, Pracownik (swój).

## Poza zakresem 011
- Import z Optimy (jest integracja przez agenta Optima — osobny moduł).
- Eksport list płac, nieobecności, umów do Optimy; dane etatowe i ubezpieczeniowe w arkuszu (R4).
- PDF karty wynagrodzeń i PIT-11 (zostają strony do wydruku); podpis PDF.
