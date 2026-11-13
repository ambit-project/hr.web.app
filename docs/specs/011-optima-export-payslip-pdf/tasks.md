# 011 Eksport do Optimy i PDF paska — zadania

[Specyfikacja](spec.md) · [Plan](plan.md)

- [ ] T1. Baza: `employees_app.optima_export` + audyt odczytu, uprawnienia w `9-grants`; testy rutyny.
- [ ] T2. API: `OptimaHrImportSheet` (NPOI), endpoint eksportu; testy arkusza i HTTP.
- [ ] T3. API: `PdfFonts` + Noto Sans (OFL, `Fonts/OFL.txt`), `PayslipPdf` (PDFsharp-MigraDoc 6.2.4 przypięte),
      endpointy PDF paska; testy.
- [ ] T4. Front: ikona eksportu na liście pracowników, „Pobierz PDF” przy paskach; tłumaczenia; `yarn api:gen`.
- [ ] T5. Dokumentacja: 010 R2 (zmiana decyzji), `db/README.md`, PRODUCT, README (licencje bibliotek), zrzut ekranu.

## Decyzje
- [D] `.xls` zamiast `.xlsx` — pomoc Optimy podaje tylko `*.xls`; tak samo ambit.es.
- [D] PDFsharp-MigraDoc przypięte na 6.2.4 (MIT według docs.pdfsharp.net). Komentarz w ambit.es mówi o zmianie
  licencji w nowszych wersjach — niepotwierdzone w źródłach; przed podbiciem sprawdzić licencję wydania.
- [?] Kolumny etatowe, ubezpieczeniowe, US, PPK, bank — puste do czasu sprawdzenia formatu na wzorcowym `kadry.xls`.
