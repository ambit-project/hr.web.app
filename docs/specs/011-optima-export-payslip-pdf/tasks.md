# 011 Eksport do Optimy i PDF paska — zadania

[Specyfikacja](spec.md) · [Plan](plan.md)

- [x] T1. Baza: `documents_app.optima_export` (`db/changelog/modules/90-documents/documents_app.optima_export.sql`)
      + audyt odczytu `optima_export`; uprawnienia schematu `documents_app` już są.
- [x] T2. API `Features/OptimaExport/` (`OptimaHrImportSheet`, `OptimaCountries`, `ExportEmployeesToOptima`); testy
      `OptimaHrImportSheetTests`, `OptimaExportHttpTests` (nagłówki, PESEL jako tekst, filtr, spółka wymagana, 403, audyt).
- [x] T3. API `Common/Pdf/PdfFonts.cs` + Noto Sans (`Common/Pdf/Fonts`, OFL), `Features/Documents/PayslipPdf.cs`,
      `GET /api/payroll-entries/{id}/payslip/pdf`; test `PayslipPdfHttpTests` (PDF się otwiera, 409 `notClosed`, 403).
- [x] T4. Front: ikona eksportu na liście pracowników (zaznaczone albo filtr, wymaga spółki), `PayslipPdfButton` na
      stronie paska i w tabelach pasków (kartoteka, Moje dokumenty); `callApiFile` w `server/api.ts`; `yarn api:gen`.
- [x] T5. Dokumentacja: 010 R2 (zmiana decyzji), `db/README.md`, PRODUCT, README (licencje bibliotek), zrzut ekranu.

## Decyzje
- [D] NPOI 2.7.x ciągnie SixLabors.ImageSharp 2.1.11 ze zgłoszonymi podatnościami; poprawki są tylko w 3.2+ na
  licencji Six Labors Split (nie open source dla większych firm). Podatności dotyczą dekodowania obrazów — eksport
  zapisuje tylko komórki tekstowe i liczbowe — więc `NuGetAuditSuppress` w `Directory.Build.props`. Testy podnoszą
  `System.Security.Cryptography.Pkcs/Xml` do 11.0.0 (w API daje je framework).
- [D] `.xls` zamiast `.xlsx` — pomoc Optimy podaje tylko `*.xls`; tak samo ambit.es.
- [D] PDFsharp-MigraDoc przypięte na 6.2.4 (MIT według docs.pdfsharp.net). Komentarz w ambit.es mówi o zmianie
  licencji w nowszych wersjach — niepotwierdzone w źródłach; przed podbiciem sprawdzić licencję wydania.
- [?] Kolumny etatowe, ubezpieczeniowe, US, PPK, bank — puste do czasu sprawdzenia formatu na wzorcowym `kadry.xls`.
