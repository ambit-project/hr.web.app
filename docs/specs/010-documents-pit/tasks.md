# 010 Dokumenty i PIT — zadania

[Specyfikacja](spec.md) · [Plan](plan.md)

- [x] T1. Baza: schematy `documents` / `documents_app` (`db/changelog/1-ddl/20261113-34-documents.sql`): tabela
      `tax_forms` (wersja, cel `filing|correction`, `data` jsonb, wysłanie, przekazanie z `given_how`). Rutyny
      w `db/changelog/modules/90-documents/`: pomocnicze `documents.require_employee_access`, `address_parts`,
      `pit11_entries`, `pit11_compute`, `pit11_employees`, `tax_form_for_write`; kontrakt `payslip`, `my_payslips`,
      `wage_card`, `pit11_generate`, `pit11_mark_sent`, `pit11_mark_given`, `tax_form_get`, `tax_form_list`,
      `my_tax_forms`, `company_tax_summary`, `consent_document`. Uprawnienia `documents_app` w `9-grants`. Testy
      `tests/HrWebApp.Api.Tests/Database/DocumentsRoutineTests.cs` (aranżacja `DocumentsKit.cs` na `PayrollKit`).
- [x] T2. API `apps/api/HrWebApp.Api/Features/Documents/` (tag `Documents`, `MyDocuments`): XML PIT-11(29) składany
      w C# (`Pit11Xml.cs`, `System.Xml.Linq`). Testy HTTP `tests/HrWebApp.Api.Tests/Features/Documents/DocumentsHttpTests.cs`
      walidują każdy wygenerowany XML schematem MF z `tests/HrWebApp.Api.Tests/Fixtures/pit11-29/` (`schemat.xsd`
      + importy PIT-R(22), StrukturyDanych v13, KodyKrajow v13, ORD-ZU(3), KodyUrzedowSkarbowychExWUS v8 pobrane
      z crd.gov.pl). Migawka OpenAPI zaktualizowana.
- [x] T3. Front `apps/web/src/features/documents/`: strony do wydruku (pasek `/payslips/$entryId`, karta wynagrodzeń
      `/wage-cards/$employeeId?year=`, PIT-11 `/tax-forms/$id`, zgoda `/consent-documents/$id`), „Płace → PIT-11”
      (`/pit11`), „Płace → Zestawienie PIT-4R/8AR” (`/tax-summary`), zakładka „Dokumenty” w kartotece, paski i PIT-11
      w „Moich dokumentach”, ikona druku przy zgodzie (006). Druk: `@media print` w `styles/base.css` chowa menu, górny
      pasek, pasek dev i `.no-print`.
- [x] T4. Dokumentacja: `db/README.md` (kody), PRODUCT, sprawdzenie w przeglądarce.

## Endpointy

| Metoda i ścieżka | Rutyna | Role |
|---|---|---|
| `GET /api/payroll-entries/{id}/payslip` | `payslip` | Kadry, Admin; Pracownik — swój |
| `GET /api/employees/{id}/payslips`, `GET /api/my/{employeeId}/payslips` | `my_payslips` | j.w. |
| `GET /api/employees/{id}/wage-card/{year}` | `wage_card` | Kadry, Admin |
| `GET /api/companies/{id}/pit11/{year}` / `POST` (`{employeeId?}`) | `tax_form_list` / `pit11_generate` | Kadry, Admin |
| `GET /api/tax-forms/{id}`, `GET /api/tax-forms/{id}/xml` | `tax_form_get` (+ C#) | Kadry, Admin; Pracownik — swój wysłany/przekazany |
| `POST /api/tax-forms/{id}/sent` (`{sentAt}`), `/given` (`{givenAt, givenHow}`) | `pit11_mark_sent`, `pit11_mark_given` | Kadry, Admin |
| `GET /api/employees/{id}/tax-forms`, `GET /api/my/{employeeId}/tax-forms` | `my_tax_forms` | j.w. |
| `GET /api/companies/{id}/tax-summary/{year}` | `company_tax_summary` | Kadry, Admin |
| `GET /api/deduction-consents/{id}/document` | `consent_document` | Kadry, Admin; Pracownik — swoja |

## Decyzje ([D] — wykonawca, [?] — do potwierdzenia)

- **Pozycje PIT-11 [D]:** `data.positions` pod nazwami elementów XSD (`P_29` …), nie `poz29` jak w planie — XML mapuje
  się 1:1. Pozycje puste nie są zapisywane (pole puste na formularzu i brak elementu w XML).
- **PESEL [D]:** nie trafia do `data` (ADR-0009 — zaszyfrowany w kartotece); `tax_form_get` odszyfrowuje go przy
  odczycie. Imię, nazwisko, data urodzenia, adres i urząd są migawką z dnia generowania.
- **Wiersze części E:** umowa o pracę 1 / do 26 lat w dniu wypłaty — 2; zlecenie 6 / 7; dzieło 5. Przychód w E =
  przychód opodatkowany (z wpłatą pracodawcy PPK, R6 [?]) minus zwolniony (objaśnienie 13). Koszty — faktycznie
  uwzględnione (KUP, KUP50). Dochód = przychód − koszty, nie mniej niż 0. Kolumna e (dochód zwolniony z umów
  międzynarodowych) nie jest wypełniana. Poz. 34/35 (koszty 50% przy umowie o pracę) — silnik 009 ich nie liczy.
- **Składki:** poz. 95 — część odliczona od dochodu (`PODST-PIT.trace.socialDeductible`) w wierszach 1, 5, 6; poz. 96
  — w wierszach 2, 7; poz. 97 — reszta (od przychodu zwolnionego). Poz. 122 — suma `ZDROW`.
- **Część G:** ulga dla młodych (do 26 lat bez `youthReliefWaiver`) → poz. 109–111; pozostałe zwolnienia → 114–116
  i znaczniki 118–120 z oświadczeń `returnRelief` / `familyRelief` / `seniorRelief` w dniu wypłaty. Poz. 121 = 2.
- **Poz. 28:** tylko przy wierszu 1 (w XSD należy do jego sekwencji — walidacja wykryła błąd przy wierszu 2); 2/4 przy
  więcej niż jednej umowie o pracę, 3/4 przy kosztach podwyższonych (warunki `increased` albo oświadczenie).
- **Adres [D]:** pole ulicy kartoteki dzielone na ulicę, nr domu i lokalu (`ul. Lipowa 1/2` → Lipowa, 1, 2); adres
  zamieszkania aktualny dziś, a bez niego ostatni.
- **Wersje (R7) [D]:** treść bez zmian → nic; ostatnia niewysłana → zastąpiona (przekazanie kasowane); ostatnia
  wysłana → nowa wersja `correction` (XML CelZlozenia 2). `tax_form_list.is_outdated` — treść ostatniej wersji różni
  się od wyliczonej teraz (np. po liście korygującej; reguła z modelu domeny „formularz do korekty”).
- **Brakujące dane (R7):** formularz powstaje z `missing` (`pesel`, `address`, `taxOffice`); oznaczenia i XML →
  409 `missingData`.
- **Pracownik (R10, R13) [D]:** widzi tylko PIT-11 wysłane albo przekazane; inny → 404. Pasek — tylko z zamkniętej
  listy (409 `notClosed` dla wszystkich).
- **Audyt odczytu:** `payslip` (pasek, lista pasków, karta z `wageCard:<rok>`), `tax_form` (formularz); zapis przez
  `platform.read_audit` — bez wpisu, gdy czyta sam pracownik. Zmiany formularzy w audycie zmian jako `tax_form`.
- **Zestawienie (R12):** miesiąc daty wypłaty, zaliczki według rodzaju umowy (korekty liczą się w rodzaju umowy),
  ryczałt ze zleceń i dzieł; bez XML.
- **Druk (R2):** strony HTML z `@media print`; „Zapisz jako PDF” w oknie drukowania. Kartka w druku zawsze biała.
- **Zgoda (R11):** treść z danych zgody 006 i kategorii składnika; dokument dla Kadr (ikona druku w sekcji zgód);
  rutyna dopuszcza też pracownika do jego zgody, ale front nie daje mu linku.

## Mapa wymaganie → test

| R | Test |
|---|---|
| R1, R3 | `DocumentsRoutineTests.Payslip_and_wage_card_come_from_closed_lists_and_the_employee_sees_only_own_documents` (pasek, `notClosed`, audyt, lista pasków pracownika bez audytu), `DocumentsHttpTests.Kadry_generate_pit11_…` (pasek i lista przez HTTP) |
| R2, R8 (wydruk) | sprawdzenie w przeglądarce (niżej) |
| R4 | `Payslip_and_wage_card_…` (składki pracodawcy, potrącenia), `Kadry_generate_pit11_…` (2 wiersze z korektą) |
| R5 | `Employment_of_an_adult_fills_row_1_with_employer_ppk_contributions_and_health`, `Person_under_26_with_relief_goes_to_part_g_and_a_waiver_moves_income_to_row_2`, `Mandate_and_specific_work_fill_rows_6_and_5_and_flat_tax_stays_out` |
| R6 | `Employment_of_an_adult_…` (PPK-PR w P_29) |
| R7 | `Unsent_form_is_replaced_and_after_sending_a_change_makes_a_correction` (missing, zastąpienie, bez zmian, `sent`, `notLatest`, korekta, `is_outdated`), `Mandate_and_specific_work_…` (`noPayments`) |
| R8 (XML) | `DocumentsHttpTests.Kadry_generate_pit11_and_every_xml_is_valid_against_the_ministry_schema` (6 plików: wiersze 1, 2, 5, 6, sama część G, korekta), `Missing_pesel_blocks_the_xml_…` (409) |
| R9 | `Unsent_form_…` (oznaczenia, `future`, `givenHow invalid`); terminy — `types.test.ts` (`pit11Deadlines`) |
| R10 | `Payslip_and_wage_card_…` (pracownik widzi PIT-11 dopiero po przekazaniu, audyt `tax_form`) |
| R11 | `Payslip_and_wage_card_…` i `Missing_pesel_…` (`consent_document`) |
| R12 | `Mandate_and_specific_work_…` (zestawienie: zaliczki zlecenia i dzieła, ryczałt 18 zł) |
| R13 | `Payslip_and_wage_card_…` (403 na cudze i na Kadrowe rutyny), `Missing_pesel_blocks_the_xml_and_pracownik_gets_403_on_forms_of_others` |

## Sprawdzenie w przeglądarce (2026-11-13, dev)

- Kadry: lista `2026/11/UP` spółki Demo **zamknięta** (dane dev zmienione — lista nie jest już do usunięcia).
- „Płace → PIT-11”, Demo 2026: 13 osób „niewygenerowany”, terminy 2027-01-31 / 2027-02-28. Generowanie dla Anny
  Testowej → wersja 1 „złożenie”; P_29 6 090,00 (6 000 + 90 PPK pracodawcy), P_30 250, P_31 5 840, P_33 302.
  Oznaczono przekazanie (2026-11-13, papierowo). Pozostałe 12 osób bez PIT-11.
- Wydruk PIT-11 (części A–H, wiersze E 1–15 z numerami pól) i pasek Anny (grupy pozycji, podstawy, brutto 6 000,00,
  netto 4 289,43, do wypłaty 4 169,43); w arkuszu stylów jest reguła `@media print` chowająca menu, górny pasek,
  pasek dev i przyciski.
- Pracownik: „Moje dokumenty” — pasek 2026/11/UP i PIT-11 2026 (przekazany); otwiera swój PIT-11.
- NIP spółki Demo w danych dev poprawiony na fikcyjny z poprawną sumą kontrolną (changeset 20261113-35), więc XML Demo przechodzi schemat.
  z prawdziwym NIP (testy) przechodzą.

## Sprawy otwarte

- R6 [?] wpłata pracodawcy PPK jako przychód ze stosunku pracy — jak w 009 R19.
- Wiersze 3, 8, 9–15, poz. 34/35, 98–108, 123 nie są wyliczane (poza zakresem albo silnik 009 ich nie zna).
- Wzór PIT-11 na rok 2026 nie jest opublikowany — używany jest PIT-11(29); zmiana schematu = nowy `Pit11Xml`
  i nowe pliki w `Fixtures`.
- Druga informacja PIT-11 (nr informacji, poz. 5) i PIT-11 w trakcie roku na wniosek (art. 39 ust. 1a) — brak.
- Pracownik nie ma linku do dokumentu swojej zgody (rutyna pozwala).
