# 010 Dokumenty i PIT — plan

[Specyfikacja](spec.md)

## Baza (`documents`, `documents_app`, moduł `db/changelog/modules/90-documents/`)

- `tax_forms` (id, employee_id, year, version, purpose `filing|correction`, data jsonb — wszystkie pozycje PIT-11 jako
  `{ "poz29": 123.45, … }` z identyfikatorami list, generated_at, sent_to_office_at, given_to_employee_at, given_how) —
  UK (employee_id, year, version).
- Funkcje: `payslip(entry_id)` (pozycje zamkniętej wypłaty + dane nagłówka; audyt odczytu), `my_payslips(employee_id)`,
  `wage_card(employee_id, year)`, `pit11_compute(employee_id, year)` → jsonb pozycji (wiersze E/G z linii 009 według
  rodzaju umowy, wieku w dniu wypłaty, ulg; koszty faktycznie uwzględnione), `pit11_generate(company_id, year,
  employee_id null)`, `pit11_mark_sent/given`, `tax_form_get/list`, `my_tax_forms`, `company_tax_summary(company_id,
  year)` (R12).

## API

XML PIT-11 składa API (C#, `System.Xml`, przestrzeń nazw schematu 13535, elementy nazwane jak w XSD — `P_29` itd.)
z `data` z bazy; `GET /api/tax-forms/{id}/xml` zwraca plik. Pozostałe: `/api/payroll-entries/{id}/payslip`,
`/api/my/{employeeId}/payslips`, `/api/employees/{id}/wage-card/{year}`, `/api/companies/{id}/pit11/{year}`
(GET lista, POST generuj), `/api/tax-forms/{id}` (+ `/sent`, `/given`), `/api/my/{employeeId}/tax-forms`,
`/api/companies/{id}/tax-summary/{year}`. Schemat XSD (z importami) zapisany w `tests/…/Fixtures/pit11-29/`; test
waliduje wygenerowany XML.

## Front

Strony do wydruku (`@media print`, bez menu): pasek `/payslips/$entryId`, karta wynagrodzeń, PIT-11, zgoda na
potrącenie. Menu „Płace → PIT-11” (spółka, rok, generowanie, lista osób ze statusem wysłania), w kartotece zakładka
„Dokumenty” (paski, karta wynagrodzeń, PIT-11), „Moje dokumenty” — paski i PIT-11. Zestawienie PIT-4R/8AR w „Płace”.
