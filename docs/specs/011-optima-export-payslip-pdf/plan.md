# 011 Eksport do Optimy i PDF paska — plan

[Specyfikacja](spec.md)

## Baza (`documents_app`, moduł `90-documents`)
- `documents_app.optima_export(p_company_id, p_ids uuid[], p_search, p_status)` — w module dokumentów, bo korzysta
  z `documents.address_parts`; wiersze: akronim, imiona, nazwisko,
  data urodzenia, PESEL (odszyfrowany kluczem jak w `employee_get`), status (archiwalny), adres zamieszkania
  i korespondencyjny bieżący z podziałem `documents.address_parts` (ulica / nr domu / nr lokalu), e-mail. Kadry
  i Administrator; `platform.read_audit` (`optima_export`).
- Pasek: istniejąca `documents_app.payslip(entry_id)` — bez zmian.

## API
- `OptimaHrImportSheet` (C#, NPOI 2.7.x — Apache-2.0, `HSSFWorkbook`): 139 kolumn z domyślnymi Optimy, walidacja Kod
  / Nazwisko / Imie, limit wierszy — przeniesione z ambit.es. `POST /api/employees/export/optima` (treść jak
  kopiowanie: `companyId`, `employeeIds`, `search`, `status`) → plik `application/vnd.ms-excel`.
- `PayslipPdf` (PDFsharp-MigraDoc `[6.2.4]`, `PdfFonts` z Noto Sans jako zasób osadzony):
  `GET /api/payroll-entries/{id}/payslip/pdf` — dostęp jak
  `payslip` (Pracownik tylko swój), więc bez osobnej ścieżki „my”.
- Testy: arkusz (nagłówki, kolejność, PESEL jako tekst, Plec z PESEL, limit, brak Kod), HTTP (role, audyt,
  no-store, Content-Type), PDF (otwiera się — nagłówek `%PDF`, zawiera kwotę „do wypłaty” po wyciągnięciu tekstu
  PDFsharp, polskie znaki).

## Front
- Lista pracowników: ikona „Eksport do Optimy” (obok kopiowania) — pobiera dla zaznaczonych albo wg filtrów.
- Pasek: ikona „Pobierz PDF” obok drukowania (strona paska, „Dokumenty” w kartotece, „Moje dokumenty”).
- Pobranie przez server function (token w cookie) → base64 → plik w przeglądarce (`lib/download.ts`).
