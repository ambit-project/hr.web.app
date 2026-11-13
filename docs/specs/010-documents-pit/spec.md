# 010 Dokumenty i PIT — specyfikacja

Model: [10-documents-and-pit.md](../../domain/10-documents-and-pit.md). Decyzje właściciela: PIT-11 jako PDF i XML
zgodny ze schematem MF na dany rok (decyzja 6), pasek tylko w samoobsłudze (decyzja 7.3.3), dokument zgody na
potrącenie do podpisu (wymaganie 10), bez wysyłki do MF (non-goal). Właściciel 2026-11-13: „rób bez pytania” —
decyzje oznaczone **[D]**; „czego nie wiemy, tego nie robimy” (009 R28).

Podstawa (stan na 13.11.2026): ustawa o PIT — art. 39 ust. 1, 42g, 45b ust. 1 pkt 2, 45ba; wzór PIT-11(29)
(bieżący według MF, schemat `http://crd.gov.pl/wzor/2024/10/15/13535/`, wersjaSchemy 4-0E, wariant 29); KP art. 85
§ 5, art. 94 pkt 9a–9b; rozporządzenie o dokumentacji pracowniczej Dz.U. 2026 poz. 474 § 6 pkt 3; ustawa o sus art.
41 ust. 8–10a, art. 47 ust. 3c; ustawa o rachunkowości Dz.U. 2026 poz. 522 art. 74 ust. 2 pkt 2.

## Wymagania

### Pasek wypłaty
- **R1.** Pasek wypłaty z wypłaty na **zamkniętej** liście (009): spółka, osoba, umowa, okres, data wypłaty, pozycje
  według stron (przychody, składki, zdrowotna, podatek, PPK, potrącenia), netto, do wypłaty, podstawy. Przepisy nie
  wymagają paska (KP art. 85 § 5 — wgląd na żądanie); jest udogodnieniem.
- **R2.** Pasek to strona do wydruku (HTML z CSS druku); PDF powstaje przez drukowanie w przeglądarce **[D: bez
  biblioteki PDF po stronie serwera — licencje i prostota]**.
- **R3.** Pracownik widzi swoje paski w „Moich dokumentach” (lista według miesięcy). Odczyt paska przez kogoś innego
  niż pracownik zapisuje audyt odczytów (`payslip`).

### Karta wynagrodzeń
- **R4.** Karta wynagrodzeń kartoteki za rok (§ 6 pkt 3 rozporządzenia wymaga karty, nie określa jej treści —
  układ **[D]**):
  wiersz na każdą wypłatę zamkniętą w roku (data wypłaty, lista, przychód, podstawa ZUS, składki pracownika
  i pracodawcy, zdrowotna, koszty, zaliczka PIT, PPK, potrącenia, do wypłaty) i suma roczna; strona do wydruku. Kadry
  i Administrator; odczyt w audycie (`payslip`).

### PIT-11
- **R5.** Informacja PIT-11(29) kartoteki za rok podatkowy z zamkniętych list z datą wypłaty w roku: część B (płatnik:
  spółka, NIP), C (podatnik: PESEL, imiona, nazwisko, data urodzenia, adres zamieszkania z kartoteki), A (urząd
  skarbowy podatnika z kartoteki), E (wiersze 1–3: umowa o pracę — przychód, koszty, dochód, dochód zwolniony,
  zaliczka, koszty 50%; wiersz 2 dla osoby do 26 lat z pobieraną zaliczką; wiersz 5: dzieło; wiersz 6–7: zlecenie;
  poz. 95–97 składki społeczne), G (poz. 109–117 przychody zwolnione z ulg; poz. 122 składka zdrowotna). Wiersz 3
  i 8 (wiek emerytalny) — poza zakresem **[D]**. Przychód z ryczałtu (≤ 200 zł) — nie wchodzi do PIT-11 (PIT-8AR).
- **R6.** Wpłata pracodawcy na PPK — przychód w dniu przekazania do instytucji finansowej (009 R19): w wierszu umowy,
  na której liście została ujęta (`PPK-PRZ`), a przekazana w roku i nieujęta na żadnej liście — w wierszu umowy bez
  zaliczki (`documents.pit11_untaxed_ppk`). Taka wpłata u osoby z ulgą (do 26 lat, 152–154) — zwolnienia system nie
  liczy: `missing` `ppkIncomeRelief`, formularza nie można oznaczyć ani pobrać.
- **R7.** Generowanie: Kadry generują PIT-11 dla spółki i roku (wszystkich osób z wypłatami) albo dla jednej osoby;
  ponowne generowanie zastępuje wersję, gdy nie została oznaczona jako wysłana; po oznaczeniu „wysłana do US” (data)
  korekta to nowa wersja z celem „korekta” (poz. 7) **[D]**. Brak PESEL albo adresu → `missingData` z listą pól.
- **R8.** Wynik: XML zgodny ze schematem 13535 (test waliduje XML schematem zapisanym w repo) i strona do wydruku
  z układem pól jak wzór. Wysyłki do MF nie ma (non-goal) — plik pobiera się z ekranu.
- **R8a.** Wzór PIT-11(29) (schemat 13535) dotyczy dochodów od 2024 r. (CRD, `wyroznik.xml`); MF podaje go jako
  bieżący na podatki.gov.pl (stan 13.11.2026). Generowanie za rok po 2026 — U0003 `pit11FormUnknown`, dopóki nowy
  rok nie zostanie sprawdzony ze wzorem MF (zasada: czego nie wiemy, tego nie liczymy).
- **R9.** Terminy (informacja na ekranie): do urzędu do końca stycznia, do podatnika do końca lutego (art. 42g ust. 1).
  Projekt zmiany (UDER105 — PIT-11 do podatnika tylko na wniosek) nie jest prawem; system pozwala oznaczyć
  przekazanie podatnikowi (data, sposób) **[D]**.
- **R10.** Pracownik widzi swoje PIT-11 w „Moich dokumentach” (strona do wydruku); odczyt w audycie (`tax_form`).

### Dokument zgody na potrącenie
- **R11.** Dla zgody (006) strona do wydruku z treścią zgody (kto, na co, kwota miesięczna, okres, podstawa — KP art.
  91) i miejscem na podpis; po podpisaniu Kadry zaznaczają datę w 006 **[D: bez przechowywania skanu]**.

### PIT-4R i PIT-8AR (zestawienie)
- **R12.** Zestawienie spółki za rok: zaliczki pobrane w każdym miesiącu (według daty wypłaty) z list umów o pracę,
  zleceń i dzieł oraz ryczałt — jako pomoc do PIT-4R(13) i PIT-8AR(14); bez XML **[D]**.

### Role
- **R13.** Kadry i Administrator — wszystko; Pracownik — tylko swoje paski i PIT-11.

## Poza zakresem 010
- Wysyłka e-Deklaracji, podpis, UPO; XML PIT-4R i PIT-8AR; informacje RMUA (ustawa o sus art. 41 ust. 8).
- PIT-11 dla wieku emerytalnego (wiersze 3, 8, 14), staży, zasiłków macierzyńskich, praw autorskich (wiersz 9).
- Przechowywanie skanów podpisanych dokumentów.
