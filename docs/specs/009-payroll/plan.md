# 009 Listy płac — plan

[Specyfikacja](spec.md)

## Baza

Schematy `payroll` i `payroll_app`, moduł `db/changelog/modules/80-payroll/`.

| Tabela | Kolumny (skrót) |
|---|---|
| `company_settings` | company_id, valid_from, accident_rate (np. 0,0167), pays_sickness_allowance — `EXCLUDE` okresów |
| `payrolls` | id, company_id, number (UK w spółce), kind, period_from, period_to, payment_date, corrects_payroll_id, status `open|calculated|closed`, is_stale, calculated_at, closed_at, created_* — UK zwykłej listy (company, kind, period_from) |
| `payroll_members` | payroll_id, employment_id, excluded (R3) |
| `entries` | id, payroll_id, employment_id, employee_id, gross, net, payout, warnings jsonb |
| `lines` | id, entry_id, seq, code, side `income|employee|employer|info|deduction`, base, rate, amount, component_type_id, trace jsonb |

Sumy roczne (R25) — **bez tabeli**: funkcje `payroll.year_totals(employee_id, year, before_payroll)` liczą z linii
zamkniętych list (ZUS po okresie listy, PIT po dacie wypłaty). Kwota ściągnięta z zajęcia — z linii `ALIM`/`KOMOR`
zamkniętych list z `trace.garnishmentId` (zastępuje `0` w `pay_components_app.garnishment_list`).

Nowe parametry prawne (dane `main`): `chorobowe.podstawa.minimalna` 4 147,10 (2026), `urlop.ekwiwalent.wspolczynnik`
20,92 (2026), `pit.niepobieranie.limit` 30 000, `ppk.prog.obnizenia` już jest, `fp.pracodawca` (= FP 1% + FS 1,45%)
już jest, `fgsp.pracodawca` już jest. 2027: płaca minimalna 4 950 / 32,30, limit 30-krotności 300 990 —
dopisać z okresem od 2027-01-01.

## Silnik (funkcje wewnętrzne `payroll.*`)

`calculate(payroll_id)` → dla każdej umowy `calc_entry(payroll, employment)`, który składa linie etapami
(tabela tymczasowa `_lines` w transakcji), każdy etap czyta linie poprzednich:

1. `stage_base_pay` — `ZASAD` (R5), pomniejszenia `ZASAD-CH` (1/30, R6) i `ZASAD-NB` (godzinowe, R8).
2. `stage_absence_pay` — `CHOR` / `ZAS-CH` / info `ZAS-ZUS` (R6, R7), `URLOP-ZM` (zmienne za urlop, R8), `SW50` (R8).
3. `stage_overtime` — `NADG50`, `NADG100` (R9).
4. `stage_components` — składniki umowy (R10), `EKWIW` (R11), zlecenie `ZLEC`, dzieło `DZIELO` (R12).
5. `stage_social` — `ZUS-E-P`, `ZUS-R-P`, `ZUS-C-P` (pracownik), `ZUS-E-PR`, `ZUS-R-PR`, `ZUS-W-PR`, `FP`, `FGSP`
   (pracodawca), info `PODST-ZUS` (R13, R14).
6. `stage_ppk` — `PPK-PR` (pracodawca, info przychód PIT), `PPK-P`, `PPK-PD`, `PPK-PRD` (R20).
7. `stage_health` — `ZDROW` (R15).
8. `stage_tax` — info `KUP`, `PODST-PIT`, `ZWOL-MLODZI`; `PIT` albo `PIT-RYCZ` (R16–R19).
9. `stage_net` — netto (R21).
10. `stage_deductions` — `ALIM`, `KOMOR`, `ZALICZKA`, `KARA`, składniki dobrowolne (R22), do wypłaty (R23).

Każda linia: `trace` z identyfikatorami źródeł i wartościami parametrów (R24). Lista korygująca (R4): `calc_entry`
dla okresu korygowanego, a zapisane linie = różnica z linią tego samego kodu na liście korygowanej (i jej
wcześniejszych korektach); PIT liczony w miesiącu daty wypłaty korekty.

## Blokady (R25)

`payroll.period_closed(employment_id, date)` i `payroll.used(kind, id)`; dopisane wywołania w rutynach 005 (zapis
i zakończenie oświadczeń), 006 (składniki, zgody, zajęcia — usuwanie), 007 (nieobecności, nadgodziny, godziny
zlecenia), 008 (usuwanie uczestnictwa), 004 (usuwanie umowy). Zmiana danych w okresie listy `calculated` ustawia
`is_stale`.

## Rutyny `payroll_app`

`company_settings_list/set`, `payroll_list(company_id, year)`, `payroll_create(company_id, kind, period, payment_date,
corrects_payroll_id)`, `payroll_get(id)` (+ podsumowanie), `payroll_members_set(id, employment_ids, excluded)`,
`payroll_calculate(id)`, `payroll_close(id)`, `payroll_delete(id)`, `entry_list(payroll_id)`, `entry_get(entry_id)`
(linie ze śladem; audyt odczytu `payslip`), `year_totals(employee_id, year)`.

## API i front

`/api/companies/{id}/payroll-settings`, `/api/companies/{id}/payrolls?year=`, `/api/payrolls/{id}` (+ `/calculate`,
`/close`, `/members`), `/api/payrolls/{id}/entries`, `/api/payroll-entries/{id}`. Front: menu „Płace → Listy płac”
(lista list, kreator nowej listy: rodzaj, okres, data wypłaty, skład), strona listy (status, sumy, tabela wypłat:
osoba, brutto, składki, zdrowotna, PIT, PPK, netto, potrącenia, do wypłaty; `>` do szczegółów wypłaty), szczegóły
wypłaty (pozycje z podstawą, stawką, kwotą; rozwijany ślad), akcje: nalicz, zamknij, usuń, korekta.

## Testy

Scenariusz z [09-payroll.md — Przeliczenia scenariusza](../../domain/09-payroll.md#przeliczenia-scenariusza) tam,
gdzie zgodny ze specyfikacją (różnice zapisać w tasks.md): Anna X i XI (choroba, nadgodziny, PIT-2, PPK), Piotr
(ulga dla młodych, 1/2 etatu), Marek (zlecenie godzinowe), Ewa X (zajęcie i karta sportowa, kwota wolna) i XII
(przekroczenie 120 000 zł), korekta października wypłacona w grudniu. Do tego: limit 30-krotności, FP poniżej
minimalnego, ryczałt ≤ 200 zł, dzieło, ekwiwalent, blokady po zamknięciu, `stale`, role.
