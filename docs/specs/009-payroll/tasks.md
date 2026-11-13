# 009 Listy płac — zadania

[Specyfikacja](spec.md) · [Plan](plan.md)

- [x] T1. Baza: schematy `payroll` / `payroll_app` (`db/changelog/1-ddl/20261113-32-payroll.sql`): `company_settings`
      (stopa wypadkowa, wypłata zasiłków, `EXCLUDE` okresów), `payrolls`, `members`, `entries` (kwoty, godziny,
      ostrzeżenia), `lines` (cechy przychodu, ślad), typy silnika `line_t` i `ctx_t`. Parametry prawne
      (`2-data/20261113-33-legal-parameters-payroll.sql`): `chorobowe.podstawa.minimalna`, `chorobowe.pracodawca.dni`
      (`.50`), `urlop.ekwiwalent.wspolczynnik`, `pit.niepobieranie.limit` i wartości 2027 (płaca minimalna, stawka
      godzinowa, 30-krotność, próg PPK, kwoty wolne — okresy 2026 zamknięte 31.12.2026). Silnik w
      `db/changelog/modules/80-payroll/` (etapy niżej), blokady w rutynach 004–008, kwota ściągnięta z zajęcia
      w `pay_components_app.garnishment_list`, uprawnienia `payroll_app`. Testy rutyn.
- [x] T2. API: `/api/companies/{id}/payroll-settings` (`company_settings_list`, `company_settings_set`),
      `/api/companies/{id}/payrolls?year=` (`payroll_list`, `payroll_create`), `/api/payrolls/{id}` (`payroll_get`,
      `payroll_delete`), `/calculate` (`payroll_calculate`), `/close` (`payroll_close`), `/members`
      (`payroll_member_list`, `payroll_members_set`), `/api/payrolls/{id}/entries` (`entry_list`),
      `/api/payroll-entries/{id}` (`entry_get` + `entry_lines`), roczne sumy kartoteki (`year_totals`); migawka
      OpenAPI, testy HTTP. Zapis listy przyjmuje `rowVersion` (NULL = bez sprawdzania).
- [x] T3. Front: „Płace → Listy płac” (lista, kreator, strona listy z wypłatami, szczegóły wypłaty ze śladem, akcje
      nalicz / zamknij / usuń / korekta, skład z wyłączaniem), ustawienia płacowe spółki, tłumaczenia kodów błędów
      i ostrzeżeń z `db/README.md`, etykiety audytu (`payroll`, `payroll_company_settings`).
- [x] T4. Dokumentacja: PRODUCT, sprawdzenie w przeglądarce, potwierdzenie spraw otwartych niżej.

## API i front (T2, T3)

- API: `apps/api/HrWebApp.Api/Features/Payroll/` (tag `Payroll`), testy `tests/HrWebApp.Api.Tests/Features/Payroll/PayrollHttpTests.cs`.
  Zapis listy (`/calculate`, `/close`, `PUT /members`, `DELETE ?rowVersion=`) przyjmuje `rowVersion` (pusta — bez
  sprawdzania). Ślad pozycji i szczegóły ostrzeżeń jako obiekt JSON (`JsonObject` → w OpenAPI `{"type": "object"}`),
  ostrzeżenia rozdzielone na `code` i `details`. Stopa wypadkowa w API jak w bazie — ułamek; front pokazuje procent **[D]**.
- Front: menu „Płace → Listy płac” (`/payrolls?companyId=&year=&tab=lists|settings`), strona listy `/payrolls/$id`,
  wypłata `/payroll-entries/$id`. Kreator **[D]**: krok 1 tworzy listę (skład liczy baza przy zakładaniu, więc bez
  listy nie ma czego pokazać w kroku „Skład”); „Anuluj” w dalszych krokach zostawia otwartą listę (komunikat w
  kreatorze), krok „Skład” zapisuje tylko różnice wyłączeń, „Podsumowanie” — „Nalicz teraz” albo „Przejdź do listy”.
  Korekta: przycisk „Korekta” przy zamkniętej liście zwykłej otwiera kreator z wybraną listą; umowy korekty wybiera
  się w kroku „Skład”. Ślad i ostrzeżenia idą z server function jako tekst JSON (TanStack Start nie przepuszcza typu
  `unknown`) i są parsowane w przeglądarce. Sumy roczne na stronie wypłaty — rok okresu listy.
- Sprawdzenie w przeglądarce 2026-11-13 (Kadry, spółka Demo): kreator utworzył listę `2026/11/UP` (13 umów), „Nalicz
  teraz” — 13 wypłat, brutto 107 706,00, do wypłaty 74 329,93; wypłata Anny Testowej: ZASAD 6 000,00, składki
  pracownika 1 288,57, PIT 302,00 (podstawa 5 017, kwota zmniejszająca 300), PPK 120,00 + 90,00 pracodawcy, potrącenie
  KARTA 120,00, do wypłaty 4 169,43; ślad ZUS-E-P i PIT czytelny (parametry z datami, oświadczenie, sumy roczne
  przed listą). Lista **zostawiona naliczona, niezamknięta** (dane dev do ponownego użycia; można ją usunąć na
  stronie listy). Ustawień płacowych spółki Demo nie dopisywano.

## Silnik

`payroll.calculate(id)` odświeża skład zwykłej listy (`members_sync`), usuwa wypłaty i dla każdej nie wyłączonej
umowy woła `calc_entry`. Wypłata to tablica pozycji `payroll.line_t[]` składana etapami (każdy etap czyta pozycje
poprzednich; ostrzeżenia to pozycje `side = 'warning'`, zapisywane do `entries.warnings`):

| Etap | Funkcja | Pozycje |
|---|---|---|
| 1 | `stage_base_pay` | `ZASAD` (wersje warunków), `ZASAD-CH` (1/30), `ZASAD-NB` (UB, UOP, NN, SW godzinowo), `SW50` |
| 2 | `stage_absence_pay` | `CHOR`, `ZAS-CH` / info `ZAS-ZUS`, `URLOP-ZM` |
| 3 | `stage_overtime` | `NADG50`, `NADG100` |
| 4 | `stage_components` | `ZLEC`, `DZIELO`, składniki umowy (kod składnika), `EKWIW` |
| 5 | `stage_social` | info `PODST-ZUS`, `PODST-CHOR`; `ZUS-E-P`, `ZUS-R-P`, `ZUS-C-P`, `ZUS-E-PR`, `ZUS-R-PR`, `ZUS-W-PR`, `FP`, `FGSP` |
| 6 | `stage_ppk` | `PPK-P`, `PPK-PD`, `PPK-PR`, `PPK-PRD` |
| 7 | `stage_health` | `ZDROW` |
| 8 | `stage_tax` | info `ZWOL-MLODZI`, `KUP` / `KUP50`, `PODST-PIT`, `PIT-ZMN`; `PIT` albo `PIT-RYCZ` |
| 9–10 | `calc_entry`, `stage_deductions` | netto i do wypłaty liczone z pozycji; `ALIM`, `KOMOR`, składniki potrąceń |

Korekta (R4): etapy 1–7 dla okresu listy korygowanej, różnice z pozycjami listy korygowanej i jej zamkniętych korekt
po kodzie i stronie (`prior_lines`, `diff_lines`), koszty = koszty okresu − koszty już ujęte, podatek od różnic
w miesiącu daty wypłaty korekty. Sumy roczne (`payroll.year_totals`) i kwota ściągnięta z zajęcia
(`payroll.garnishment_collected`) — z pozycji zamkniętych list, bez tabeli.

## Decyzje i odstępstwa ([D] — decyzja wykonawcy)

Zasada właściciela 2026-11-13 „czego nie wiemy, tego nie robimy”: wszystkie dawne punkty [?] sprawdzone w źródłach
(tekst jednolity ze strony Sejmu, akty wykonawcze); potwierdzone zostały, niepotwierdzone nie są liczone — ostrzeżenie
blokujące zamknięcie (R28). Wynik w tabeli „Weryfikacja [?]” niżej.

- **Ekwiwalent (R11):** godzinowy = (zasadnicze + średnia zmiennych) ÷ (dni pracy roku ÷ 12 × etat) ÷ 8, bez
  zaokrąglania kwot pośrednich (R1997 § 19 ust. 2–3 nie przewiduje zaokrąglenia); przy składnikach umowy — nie
  liczony (`equivalentComponentsUnknown`).
- **Korekta a blokady (R4, R25) [D]:** dane zamkniętego okresu można zmienić tylko wtedy, gdy istnieje otwarta albo
  naliczona lista korygująca tej listy z tą umową w składzie; po zamknięciu korekty okres znów jest zablokowany.
  Korekta nie liczy potrąceń (ostrzeżenie `deductionsNotRecalculated`, gdy kartoteka ma aktywne zajęcie), umowa bez
  różnic nie dostaje wypłaty, ujemne netto — ostrzeżenie `negativeCorrection`. PPK korekty — stawki z dnia wypłaty
  listy korygowanej. Korekty korekty nie ma (koryguje się listę zwykłą; kolejne korekty liczą różnice do wszystkich
  wcześniejszych zamkniętych).
- **Kwota zmniejszająca (R16) [D]:** raz w miesiącu daty wypłaty — bierze ją pierwsza **naliczona** (albo zamknięta)
  lista; lista naliczona później w tym miesiącu (także korekta) jej nie dostaje (`PIT-ZMN`).
- **Blokady [D]:** poza wymienionymi w R25 także nowa wersja warunków i usunięcie ostatniej wersji (004) w okresie
  zamkniętej listy, przesunięcie początku albo końca umowy (`employment_update`, `employment_terminate`) w okres
  zamkniętej listy (wyjątek korekty jak dla innych danych; w okresie listy naliczonej — `stale`, decyzja koordynatora
  2026-11-13); ustawienia płacowe spółki od miesiąca zamkniętej listy. Oświadczenia podatkowe — po miesiącu daty
  wypłaty, reszta — po okresie listy. Godziny zlecenia należą do miesiąca końca wpisu.
- **Usunięcie listy [D]:** także naliczonej (spec: „otwartą można usunąć”), zamkniętej nie.
- **Audyt:** `audit_entries.action` dopuszcza tylko create/update/delete — naliczenie, skład i zamknięcie zapisują
  `update` z kluczem `operation` (`calculate`, `members`, `close`).
- **Składniki stałe umowy o pracę (R10) [D]:** pełne za pełny miesiąc, w części miesiąca proporcjonalnie do godzin
  kalendarza, minus 1/30 za dzień choroby (gdy w podstawie chorobowego) i godziny bez wynagrodzenia (jak R8).
  Procent zasadniczego — od zasadniczego po pomniejszeniach (zlecenie — od `ZLEC`).
- **Urlop (R8) [D]:** do średniej zmiennych tylko nadgodziny i stawka × godziny; procent zasadniczego nie, bo
  zasadnicze za urlop nie jest pomniejszane (byłoby podwójnie). Średnia godzinowa do 0,0001 zł.
- **Zlecenie ryczałtowe (R12) [D]:** kwota z warunków to ryczałt miesięczny, w części miesiąca proporcjonalnie do dni;
  zlecenie godzinowe — potwierdzone godziny z końcem wpisu w okresie. Dzieło — w miesiącu końca umowy (tylko wtedy
  w składzie listy).
- **Choroba (R6, R7) [D]:** dni w roku liczone ze wszystkich umów o pracę kartoteki; zlecenie (dobrowolne chorobowe) —
  zasiłek od pierwszego dnia niezdolności po okresie wyczekiwania 90 dni; przypadki nieustalone — ostrzeżenia
  blokujące (`sicknessWaitingPeriod`, `sicknessBaseUnknown`, `sicknessBaseMissing`, `sicknessBaseSupplement`,
  `sicknessBaseFteChange`). Ciągłość ubezpieczenia u płatnika — kolejne umowy tego samego rodzaju kartoteki bez
  przerwy (`payroll.insured_from`); miesiąc „znany” — zamknięta lista spółki tego rodzaju za miesiąc (umowa o pracę —
  z wypłatą tej umowy). Miesiąc
  pomijany, gdy przepracowane godziny (wymiar − nieobecności) < ½ wymiaru miesiąca × etat. Składki w podstawie —
  proporcjonalnie, gdy część przychodu ze składkami nie jest w podstawie chorobowego. Limity dni w parametrach
  `chorobowe.pracodawca.dni` (33) i `.50` (14).
- **FP i FGŚP (R14) [D]:** zwolnienie od miesiąca po miesiącu ukończenia 55 / 60 lat; płeć z 10. cyfry PESEL. FP
  i FGŚP tylko przy ubezpieczeniach `fp` / `fgsp` w warunkach (004 R12).
- **PIT:** ryczałt 12% od przychodu (art. 30 ust. 1 pkt 5a — potwierdzone); koszty 50% — limit art. 22 ust. 9a,
  ponad limit nie liczone (`authorCostsLimitReached`); `noAdvances` — bez zaliczki do 30 000 zł, po przekroczeniu
  zaliczka bez kwoty zmniejszającej (art. 31c ust. 3), miesiąc przekroczenia nie liczony (`noAdvancesThresholdCrossed`);
  składki odliczane w części przypadającej na przychód opodatkowany przy częściowym zwolnieniu [D].
- **Potrącenia (R22):** granice do grosza w dół; zlecenie i dzieło bez etatu przy kwocie wolnej; zajęcie z całą
  kwotą ściągniętą przechodzi przy zamknięciu listy w `paidOff` z datą wypłaty.
- **Parametry 2027:** płaca minimalna 4 950 zł i 32,30 zł/h (Dz.U. 2026 poz. 1213), 30-krotność 300 990 zł (M.P. 2026
  poz. 1098), próg PPK 5 940 zł (1,2 × płaca minimalna, ustawa o PPK art. 27 ust. 2), dni pracy 2027 = 251 (§ 19
  ust. 2). Usunięte (changeset 20261113-37), bo nieogłoszone albo zależne od nieustalonego zaokrąglenia: podstawa
  minimalna chorobowego 2027 i kwoty wolne od potrąceń 2027 — naliczenie, które ich potrzebuje, kończy się `U0006`.
- **Składka zdrowotna (R15):** obniżenie do zaliczki 2021 liczone w przypadku ustalonym (`payroll.health_cap`),
  w innych — `healthCapUnknown`. Najpierw sprawdzenie „na pewno bez obniżenia” (składka ≤ 17% podstawy minus
  największa możliwa kwota zmniejszająca), więc zwykłe pensje nie dostają ostrzeżenia.
- **Wpłata PPK pracodawcy (R19):** data przekazania na liście (`payroll.payrolls.ppk_paid_on`,
  `payroll_app.payroll_ppk_paid`), przychód `PPK-PRZ` ze śladem `sourceEntryIds`; PIT-11 bierze `PPK-PRZ` i wpłaty
  przekazane w roku, nieujęte na liście (`documents.pit11_untaxed_ppk`).

## Weryfikacja [?] (2026-11-13)

| Punkt | Wynik | Źródło |
|---|---|---|
| R6 pełny miesiąc choroby → 0 | potwierdzone | R1996 § 11 ust. 1 (tylko przepracowana część miesiąca), KP art. 80 |
| R7 podstawa bez uzupełniania, z warunków bez historii | częściowo: uzupełnianie wymagane — nie liczone; przed pełnym miesiącem — potwierdzone; brak historii — nie liczone | ustawa zasiłkowa art. 36–38, 40, 48–49 (Dz.U. 2026 poz. 854) |
| R6 okres wyczekiwania (znalezione przy weryfikacji) | nie liczone w okresie | ustawa zasiłkowa art. 4, KP art. 92 § 3 |
| R9 nadgodziny normalne + dodatek | potwierdzone dla samego zasadniczego; przy innych składnikach nie liczone | KP art. 151¹ § 1 i 3, R1996 § 4a |
| R11 współczynnik 20,92 | zmienione na 251 ÷ 12 bez zaokrąglenia | R1997 § 19 ust. 2 (Dz.U. 2003 nr 230 poz. 2290) |
| R15 ograniczenie zdrowotnej | liczone w przypadku ustalonym, inne — blokada | ustawa o świadczeniach art. 83 (Dz.U. 2025 poz. 1461), PIT 2021 art. 32, 41 |
| R18 ryczałt 12% | potwierdzone | PIT art. 30 ust. 1 pkt 5a |
| R18 koszty 50% ponad limit | nie liczone | PIT art. 22 ust. 9a (limit kosztów, brak reguły ponad limit) |
| R16 noAdvances w miesiącu przekroczenia | nie liczone; po przekroczeniu bez kwoty zmniejszającej | PIT art. 31c ust. 3 |
| R19 PPK w miesiącu listy | zmienione: przychód w dniu przekazania | stanowisko MF 2019 (podatki.gov.pl), ustawa o PPK art. 26 ust. 5 |
| Parametry 2027 | płaca minimalna, 30-krotność, próg PPK — potwierdzone; podstawa chorobowego i kwoty wolne — usunięte | Dz.U. 2026 poz. 1213, M.P. 2026 poz. 1098 |

## Różnice od przeliczeń scenariusza (docs/domain/09-payroll.md)

| Przypadek | Scenariusz | Implementacja | Powód |
|---|---|---|---|
| Anna X | PIT 459, netto 5 280,29 | zgodne (wpłata IX przekazana 10.10) | — |
| Anna XI | podstawa chorobowego 6 076,25, CHOR 810,15, brutto 7 341,40, zdrowotna 580,14, PIT 457, netto 5 278,19 | 6 083,45, 811,10, 7 342,35, 580,22, 457, 5 279,06 | podstawa ze średniej faktycznych składek z 10 zamkniętych list I–X 2026 (scenariusz: 12 miesięcy z 13,71%) |
| Korekta X (wypłata 10.12) | PIT 52, netto 330,62 | zgodne | — |
| Dzieło Anny w Becie | okres listy 1–18.12 | okres = miesiąc, wypłata w miesiącu końca umowy | R1, R12 |

Zgodne ze scenariuszem: Piotr (netto 2 041,62, koszt pracodawcy z FGŚP 2,60, bez FP), Marek X (2 247,76), Ewa X
(netto 8 128,87, komornik 4 064,43, do wypłaty 3 944,44), Ewa XI (komornik 1 935,57, do wypłaty 6 073,30, zajęcie
spłacone), Ewa XII (PIT 1 618, netto 7 444,87), dzieło (netto 1 880,00), składki i zdrowotna Anny.

## Mapa wymaganie → test

Testy rutyn: `tests/HrWebApp.Api.Tests/Database/Payroll*RoutineTests.cs` (aranżacja `PayrollKit.cs`).

| R | Test |
|---|---|
| R1 | `PayrollRoutineTests.Create_numbers_regular_lists_and_rejects_duplicates_and_bad_input` (numer, okres = miesiąc, `duplicate`, `beforeStart`, `payment_date_late`) |
| R2 | `Status_flow_open_calculated_closed_with_delete_only_before_closing` (`notCalculated`, przeliczenie, `closed`, usunięcie, wersja wiersza), `Excluded_contract_…` (`stale` po wyłączeniu i po nieobecności), `Deleting_a_contract_…`, `Sickness_over_33_days_…` (ustawienia spółki → `stale`) |
| R3 | `Excluded_contract_is_not_calculated_and_changes_make_a_calculated_list_stale` (umowa zakończona przed okresem, wyłączenie, `notFound`), `Marek_hourly_…`, `Specific_work_…` |
| R4 | `PayrollCorrectionRoutineTests.October_correction_paid_in_december_records_differences_with_december_tax`, `Correction_without_changes_has_no_entries_and_needs_a_closed_regular_list` |
| R5 | `Anna_october_…`, `Part_month_by_norm_hours_terms_change_by_days_and_percent_component` |
| R6 | `Anna_november_…` (1/30, CHOR), `Sickness_over_33_days_switches_to_allowance_paid_by_zus_or_company` (33 dni, ZAS-ZUS / ZAS-CH) |
| R7 | `Anna_november_…` (średnia z zamkniętych list), `Sickness_over_33_days_…` (bez list — z warunków), `Sickness_within_a_month_of_the_previous_one_keeps_its_base` (art. 43) |
| R8 | `Unpaid_leave_force_majeure_and_leave_with_variable_pay_from_closed_months`, `Excluded_contract_…` (UB) |
| R9 | `Anna_november_…` (NADG50 281,25), `Unpaid_leave_…` (wrzesień) |
| R10 | `Part_month_…` (procent zasadniczego), `October_correction_…` (kwota stała), `Ewa_bailiff_…` (potrącenie dobrowolne) |
| R11 | `Leave_equivalent_in_the_month_the_contract_ends` |
| R12 | `Marek_hourly_…`, `Small_mandate_…`, `Specific_work_is_paid_in_the_month_it_ends_with_author_costs` |
| R13 | `Anna_october_…`, `Pension_and_disability_stop_at_the_annual_limit` |
| R14 | `Piotr_…` (FP poniżej minimalnej), `Ewa_december_…` (kobieta 55+), `Reduced_ppk_rate_…` (`peselMissing`) |
| R15 | `Anna_october_…`, `Anna_november_…` (CHOR w podstawie), `Piotr_…` (pełna składka przy zwolnieniu z PIT, bez ostrzeżeń) |
| R16 | `Anna_october_…`, `Ewa_december_crosses_the_120000_threshold`, `Youth_relief_waiver_and_higher_rate_declarations_change_the_advance`, `October_correction_…` (bez drugiej kwoty zmniejszającej) |
| R17 | `Piotr_half_time_youth_relief_without_fp_and_tax`, `Youth_relief_waiver_…` |
| R18 | `Marek_hourly_…` (20%), `Small_mandate_up_to_200_is_flat_taxed_unless_the_person_is_an_employee`, `Specific_work_…` (50%) |
| R19 | `Anna_october_…` (PPK pracodawcy w podstawie PIT) |
| R20 | `Anna_october_…`, `Ewa_…` (2% + 1%), `Reduced_ppk_rate_is_ignored_above_the_threshold_and_missing_pesel_warns` |
| R21, R23 | wszystkie przypadki z netto i do wypłaty |
| R22 | `PayrollDeductionRoutineTests.Ewa_bailiff_takes_half_of_net_then_the_rest_and_the_garnishment_is_paid_off`, `Alimony_goes_first_up_to_three_fifths_and_leaves_nothing_for_other_enforcement` |
| R24 | `Entry_details_show_lines_with_trace_and_audit_the_read` |
| R25 | `Moving_contract_dates_into_a_closed_period_is_rejected_unless_corrected` (początek, koniec, `stale`, wyjątek korekty), `Closed_list_blocks_source_changes_and_deleting_used_data` (nieobecność, nadgodziny, godziny zlecenia, oświadczenie, usunięcie umowy i uczestnictwa PPK), `Ewa_bailiff_…` (kwota ściągnięta, usunięcie zajęcia), `October_correction_…` (składnik tylko przy otwartej korekcie), sumy roczne w `Piotr_…`, `Pension_…`, `Ewa_december_…`, `Specific_work_…` |
| R26 | `Payroll_routines_are_for_kadry_and_administrator_only`, audyt odczytu w `Entry_details_…` |
| R27 | brak danych deweloperskich list — nic do testu |

Testy modułów 004–008 po dopisaniu blokad przechodzą (`~TaxDeclaration|~PayComponent|~TimeAbsence|~Ppk|~Employment`).

## Otwarte

- Przypadki nie liczone (R28) — do zrobienia po sprawdzeniu przepisów: uzupełnianie podstawy chorobowego, nadgodziny
  i ekwiwalent przy składnikach, obniżenie zdrowotnej przy PIT-2 1/24 i 1/36 i kilku wypłatach, koszty 50% ponad
  limit, miesiąc przekroczenia 30 000 zł przy `noAdvances`, podstawa minimalna chorobowego i kwoty wolne 2027.
- Testy zasady: `PayrollUnverifiedRoutineTests`, `Kadry_record_the_ppk_payment_date_of_a_closed_list`.
- Nie testowane wprost: `PPK-PRD` (wpłata dodatkowa pracodawcy), zaliczki pieniężne i kary w potrąceniach,
  `NADG100`, zasiłek przy zleceniu.
- Nowa umowa trafia na listę dopiero przy przeliczeniu (dodanie umowy nie oznacza listy jako `stale`).
