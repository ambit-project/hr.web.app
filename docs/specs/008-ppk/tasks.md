# 008 PPK — zadania

[Specyfikacja](spec.md) · [Plan](plan.md)

- [x] T1. Baza: schematy `ppk` / `ppk_app`, tabele (`company_settings` z `EXCLUDE`, `participations`, `rate_periods`
      z `EXCLUDE`, `declarations`, `reenrollment_cycles`), daty cyklu ponownego zapisu 2023 i 2027 w parametrach
      prawnych (`main`), dane deweloperskie (R16), funkcje wewnętrzne (`employed_days`, `is_employed_person`,
      `three_months_reached_on`, `enrollment_deadline`, `age_on`, `effective_from`, `status_on`, `rates_on`,
      `rebuild_rates`, `cycle_get`, `reenrollment_rows`), rutyny kontraktu, uprawnienia. Testy rutyn.
- [x] T2. API: ustawienia PPK spółki, lista „do zapisu”, PPK kartoteki (odczyt, zapis, usunięcie pomyłki, deklaracje),
      ponowny zapis (odczyt, informacja, wznowienie), „moje” PPK; migawka OpenAPI. Testy HTTP.
- [ ] T3. Front: menu „Kadry → PPK” (Ustawienia, Do zapisu, Ponowny zapis), zakładka PPK kartoteki, „Moje dokumenty”,
      etykiety audytu (`ppk_company_settings`, `ppk_participation`, `ppk_declaration`, `ppk_reenrollment_cycle`)
      i kody błędów z `db/README.md`.
- [ ] T4. Dokumentacja: PRODUCT, sprawdzenie w przeglądarce.

## Odstępstwa od planu

- Okresy stawek są stanem pochodnym: uczestnictwo trzyma stawki początkowe (`initial_basic_rate`,
  `initial_additional_rate`), a `ppk.rebuild_rates` odbudowuje `rate_periods` po każdym zapisie, deklaracji, usunięciu
  i wznowieniu z cyklu. Dzięki temu usunięcie pomyłkowej deklaracji (także jednej z dwóch złożonych w tym samym
  miesiącu) przywraca właściwe okresy bez ręcznego „otwierania” poprzedniego. Deklaracje z tym samym dniem działania
  dają jeden okres; wniosek o wznowienie i wznowienie z cyklu otwierają nowy okres z ostatnimi stawkami (R10).
- Daty cyklu w `platform.legal_parameters` (tabela trzyma liczby): wartość = data `RRRRMMDD`, okres = rok cyklu
  (`ppk.ponowny.zapis.informacja` 20270228, `ppk.ponowny.zapis.wznowienie` 20270401; także 2023 — pierwszy cykl
  ze specyfikacji, potrzebny do testu wznowienia, bo daty 2027 są jeszcze z przyszłości). Rok bez obu dat w tym samym
  roku → U0001 `{"year": ["notCycleYear"]}`.
- Bez osobnej rutyny `my_ppk`: odczyt PPK kartoteki to trzy funkcje (`participation_get`, `rate_period_list`,
  `declaration_list`) z dostępem Kadr, Administratora i Pracownika do własnej kartoteki; `GET /api/my/{employeeId}/ppk`
  i `GET /api/employees/{id}/ppk` zwracają ten sam kształt. Odczyt cyklu rozdzielony na `reenrollment_get` (daty
  i stan) i `reenrollment_list` (osoby); endpoint składa je w jedną odpowiedź.
- `POST /api/companies/{id}/ppk/reenrollment/{year}/informed` (body `informedAt`) i `…/apply` zamiast jednego
  „POST informed / POST apply” bez ścieżek — dwie różne operacje. `apply` zwraca liczbę wznowionych teraz;
  powtórzenie wznawia tylko brakujących.
- Ustawienia spółki: nowy okres z tym samym `validFrom` co ostatni zmienia ten okres (jak parametry prawne); wcześniejszy
  niż ostatni → `beforeCurrent`.
- 3 miesiące (R3): dzień X, w którym dni zatrudnienia z 12 miesięcy wstecz `(X − 1 rok, X]` jest nie mniej niż dni
  okresu 3 miesięcy kończącego się w X (od 15.01 → 14.04, od 1.03 → 31.05). Liczy się bieżący okres: po ostatnim dniu
  zatrudnienia, w którym warunek nie był spełniony. Lista „do zapisu” patrzy 92 dni naprzód; komu 3 miesiące nie
  upłyną (koniec umowy), tego nie pokazuje. Status `waiting` dla osób przed upływem 3 miesięcy (R5: „z datą, kiedy je
  osiągną”).
- Wiek (R4, R7) liczony w dniu listy albo zapisu, nie w pierwszym dniu zatrudnienia; osoby poniżej 18 lat (także na
  zleceniu) nie trafiają na listę, zapis → `tooYoung`. 14 dni (R3): zapis wymaga co najmniej 14 dni zatrudnienia przed
  dniem zapisu (`tooEarly`).
- Stany wpłat (R11): o statusie decyduje ostatnia deklaracja stanu (rezygnacja, wniosek o wznowienie, wznowienie
  z cyklu) działająca w dniu, przy tym samym dniu — później złożona. Druga rezygnacja po rezygnacji → 409
  `alreadyResigned`, chyba że w roku jej złożenia spółka przekazała informację o ponownym zapisie (art. 23 ust. 7).
  Rezygnacja złożona po informacji usuwa już wykonane wznowienie z cyklu (późniejsze niż jej działanie).
- Ponowny zapis (R13): na liście są osoby zatrudnione w ostatnim dniu na informację (albo już wznowione), grupa z wieku
  w tym dniu (urodzony 10.02.1972 → 55 lat 28.02.2027 → `requestOnly`). Osoby z rezygnacją przed zapisem też są
  wznawiane (`autoRestart` wraca je na listę „do zapisu”). Wznowienie wymaga daty informacji (`notInformed`); zmiana
  daty po wznowieniu → `applied`.
- Korekta zapisu (R6a, decyzja koordynatora): `ppk_app.participation_delete` / `DELETE /api/employees/{id}/ppk?rowVersion=`
  usuwa uczestnictwo z okresami stawek, deklaracje zostają; `rowVersion` uczestnictwa jest w `GET …/ppk`. Poprawka
  daty = usunięcie i ponowny zapis.

## Mapa wymaganie → test

Testy rutyn: `tests/HrWebApp.Api.Tests/Database/PpkRoutineTests.cs`; HTTP:
`tests/HrWebApp.Api.Tests/Features/Ppk/PpkHttpTests.cs`.

| R | Test |
|---|---|
| R1 | `Company_settings_periods_start_on_first_day_and_do_not_overlap` (1. dzień, `beforeCurrent`, 0–2,5%, zamknięcie poprzedniego), HTTP `Kadry_set_up_company_ppk_…` |
| R2 | `Employed_person_is_adult_employment_and_mandate_with_pension_and_disability` (umowa o pracę od 18 lat, zlecenie z/bez emerytalnego, dzieło) |
| R3 | `Three_months_count_twelve_months_back_with_gap_and_two_titles` (15.01 → 14.04 → 10.05, 1.03 → 31.05, zlecenie + umowa z przerwą, dzieło się nie liczy); `Age_decides_…` (`tooEarly`) |
| R4 | `Age_decides_automatic_enrollment_request_or_none` (30 lat — `toEnroll`, 60 — `requestOnly`, 71 i 17 — poza listą) |
| R5 | `Three_months_…` (`waiting`, `toEnroll`, `overdue`), `Age_decides_…`, `Resignation_stops_…` (rezygnacja przed zapisem zdejmuje z listy), HTTP `Kadry_set_up_company_ppk_…` (termin 10.04) |
| R6 | `Age_decides_…` (zapis, `alreadyEnrolled`, stawka 0,4 `invalid`), `Reduced_and_additional_…` (okresy bez nakładania) |
| R6a | `Mistaken_participation_is_deleted_with_rate_periods_and_declarations_stay` (wersja wiersza, audyt, ponowny zapis), HTTP `Kadry_set_up_company_ppk_…` (usunięcie z `rowVersion`) |
| R7 | `Age_decides_…` (`requestMissing`, po wniosku zapis, `tooOld`, `notEmployed`) |
| R8 | `Resignation_stops_…` (`rates_on` od dnia zapisu; 009 woła w dniu wypłaty) |
| R9 | `Resignation_stops_…` (rezygnacja 20.06 → od 1.06, wznowienie 3.09 → od 1.10), `Reduced_and_additional_…` (od następnego miesiąca, `future`, `rate` `invalid`), HTTP `Kadry_set_up_…` (`autoRestart` niedozwolony) |
| R10 | `Reduced_and_additional_…` (dwie deklaracje w maju → jeden okres od 1.06, `notEnrolled`, usunięcie z wersją wiersza odbudowuje okresy), `Resignation_stops_…` (`alreadyResigned`, `notResigned`, nowy okres po wznowieniu) |
| R11 | `Resignation_stops_…` (`status_on`: notEnrolled / active / resigned; `rates_on` zera przy rezygnacji, 1,5% pracodawcy), `Reduced_and_additional_…` (wpłata dodatkowa pracodawcy z ustawień) |
| R12 | `Reenrollment_2027_splits_by_age_and_2023_cycle_restarts_from_april` (`notCycleYear`), HTTP `Kadry_read_reenrollment_cycle_…` (28.02.2027, 1.04.2027) |
| R13 | `Reenrollment_2027_…` (do 55 / 55–69 / 70+, urodzony 10.02.1972 → `requestOnly`, `notInformed`, informacja po 1.04 `invalid`, wznowienie od 1.04, powtórzenie = 0, rezygnacja po informacji przed i po wznowieniu, `applied`) |
| R14 | `Pracownik_reads_own_ppk_only_and_dev_data_matches_r16`, HTTP `Pracownik_reads_own_ppk_and_gets_403_elsewhere` |
| R15 | audyt: `Reduced_and_additional_…` (utworzenie i usunięcie deklaracji); role: `Pracownik_reads_own_…` (zapis i ustawienia → U0004), HTTP 403 |
| R16 | `Pracownik_reads_own_ppk_only_…` (Anna Testowa: od 2026-04-10, 2% / 0%, pracodawca 1,5% / 0%; Piotr Kowalski — `resigned`), HTTP `Pracownik_reads_own_ppk_…` |
