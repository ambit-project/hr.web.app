# 011 Integracja z Comarch ERP Optima przez agenta — specyfikacja

Decyzje: [ADR-0010](../../adr/0010-optima-agent-integration.md). Klient po drugiej stronie to **Ambit Optima
Integrator** — usługa Windows u klienta, obok SQL Servera z Optimą (osobne repo, MIT). Jego opis kontraktu HTTP
(`docs/hr-application-api.md` w repo integratora) i ta specyfikacja opisują ten sam kontrakt; źródłem prawdy dla
klienta jest dokument OpenAPI hr.web.app (`/openapi/v1.json`, tag `OptimaAgent`).

## Cel

Kadry w hr.web.app:
- pobierają z Optimy **wszystkie rodzaje płac** (typy wypłat: dodatki i potrącenia, także nieaktywne i wycofane)
  każdej firmy i wiążą je ze składnikami hr.web.app,
- **wysyłają do Optimy** pracowników, potrącenia i dodatki,
- **pobierają z Optimy listy płac** firmy za miesiąc, z wypłatami i elementami, do podglądu.

hr.web.app nie łączy się z Optimą: agent pyta o pracę **co 500 ms**, wykonuje ją u klienta i odsyła wynik. Żaden
port u klienta nie jest otwarty.

## Wymagania

### Agent i klucz
- **R1.** Administrator zakłada klucz agenta (nazwa 1–100 znaków). Token (`optima_` + 64 znaki hex) jest pokazany
  raz; baza przechowuje tylko skrót SHA-256 i początek tokenu do rozpoznania na liście. Administrator odwołuje klucz
  (nieodwracalne). Lista kluczy pokazuje, kiedy agent ostatnio pytał o pracę i w jakiej wersji.
- **R2.** Endpointy agenta (`/api/optima/agent/…`) przyjmują tylko nagłówek `X-Optima-Agent-Key` z aktywnym
  kluczem; brak, zły format, nieznany albo odwołany klucz → `401`. Sesja użytkownika nie otwiera endpointów agenta,
  a klucz agenta nie otwiera endpointów użytkowników (`401`).
- **R3.** `GET connection` sprawdza klucz i niczego nie zmienia (`204`).

### Kolejka operacji
- **R4.** Rodzaje: `ReadPayrollComponents` (wszystkie bazy), `AddEmployees`, `ExportDeductions`, `ExportAllowances`,
  `ReadPayrolls` (jedna spółka). Stany: `pending` → `claimed` → `succeeded` / `failed`.
- **R5.** `claim` wydaje najstarszą oczekującą operację z rodzajów, które agent zgłasza w `supportedKinds` (bez
  włączonego zapisu agent nie zgłasza rodzajów zapisu). Dwa równoległe `claim` nie dostają tej samej operacji.
  Brak pracy → `204`.
- **R6.** Pusty `claim` jest lekki: jeden odczyt indeksu, bez audytu; „ostatnio widziany” zapisuje się najwyżej raz
  na minutę albo przy zmianie wersji agenta.
- **R7.** Dane operacji (`employees`, `deductions`, `allowances`, `payroll-period`) dostaje tylko agent, który ją
  trzyma (stan `claimed`), i tylko dla jej rodzaju; inaczej `404` (dla agenta ostateczny).
- **R8.** `result` zapisuje wynik próby: stan, kod błędu, log (kody i identyfikatory). Powtórzony wynik tej samej
  próby i wynik starszej próby są ignorowane (`204`). Operacja nieistniejąca albo trzymana przez inny klucz → `404`.
  Zły kształt danych → `400` (agent nie ponawia). Treść do 20 MB.
- **R9.** Operator (Kadry, Administrator) zleca operację, widzi listę (filtr rodzaju, stanu, spółki, stronicowanie)
  i szczegóły z logiem ostatniej próby oraz pozycjami pominiętymi. Ponowna próba: operacja `failed` albo `claimed`
  (agent nie odpowiedział) wraca do `pending` z tym samym id i `attempt + 1`; oczekująca → `409 pending`, udana →
  `409 succeeded`.

### Spółki i bazy Optimy
- **R10.** Spółka hr.web.app odpowiada jednej bazie firmy Optimy (1:1). Agent zgłasza bazy z NIP przy
  `ReadPayrollComponents` i `ReadPayrolls`. Nowa baza z NIP wolnej spółki wiąże się z nią sama (wpis audytu).
- **R11.** Administrator wiąże i rozwiązuje powiązanie ręcznie. Baza zgłoszona z innym NIP niż spółka →
  `400 taxNumberMismatch`; baza powiązana z inną spółką → `400 duplicate`.
- **R12.** Operacja dla jednej spółki wymaga powiązania (`400 notLinked`), a NIP zgłoszony dla bazy musi się zgadzać
  z NIP spółki (`409 taxNumberMismatch`).

### Rodzaje płac
- **R13.** `ReadPayrollComponents` zapisuje wszystkie rodzaje płac każdej bazy (także nieaktywne i wycofane, z
  flagami). Ponowny odczyt aktualizuje dane, nie dubluje ich. Rodzaj, którego nie ma w odczycie, dostaje
  `missingSince` i zachowuje powiązanie; gdy wróci — traci `missingSince`.
- **R14.** Kadry wiążą rodzaj płacy z **istniejącym** składnikiem słownika (006): dodatek z dodatkiem, potrącenie
  z potrąceniem (`kindMismatch`), składnik aktywny (`inactive`), jeden składnik na rodzaj w bazie (`duplicate`).
  Import nie zakłada składników w słowniku — cechy prawne (ZUS, PIT) ustawia Administrator (006 R3).
- **R15.** Rodzaj, który w Optimie zmienił się z dodatku na potrącenie (albo odwrotnie), traci powiązanie.

### Eksport do Optimy
- **R16.** `AddEmployees`: wskazane kartoteki spółki albo — bez wskazania — wszystkie z umową (każdego rodzaju)
  niezakończoną przed dniem zlecenia; pusta lista → `400 required`; kartoteka innej spółki → `400 notFound`.
  Dane do Optimy: akronim, imię, nazwisko, data urodzenia, PESEL, e-mail, adres zamieszkania, dokument
  (`IdentityCard`, gdy ma format dowodu osobistego, inaczej `Other` — Optima go nie zapisze), stanowisko i okres
  umowy (najwcześniejsza niezakończona, a bez niej ostatnia). Obywatelstwa hr.web.app nie prowadzi (puste).
- **R17.** Odszyfrowane dane kartoteki dostaje tylko agent; każdy odczyt jest w audycie odczytów (zasób
  `optima_export`) w imieniu operatora, który zlecił operację.
- **R18.** `ExportDeductions` / `ExportAllowances` za miesiąc: składniki umów spółki danego rodzaju, które
  obowiązują choć jeden dzień miesiąca, z kwotą = wartość składnika. Pomijane (zapisane przy operacji jako
  `skippedItems`): `notFixed` (procent, stawka × godziny — bez kwoty miesięcznej), `notMapped` (składnik bez
  rodzaju płacy Optimy), `payTypeInactive` (rodzaj nieaktywny, wycofany albo usunięty w Optimie).
- **R19.** Identyfikator pozycji to id składnika umowy — integrator wylicza z niego i miesiąca klucz importu,
  więc ponowny eksport tego miesiąca nie zapisuje pozycji drugi raz.

### Listy płac z Optimy
- **R20.** `ReadPayrolls` za rok i miesiąc **deklaracji** Optimy (`LPL_Rok`/`LPL_Miesiac` — lista za październik
  wypłacana w listopadzie ma miesiąc 11). Wynik zastępuje poprzedni import tej bazy za ten miesiąc: listy usunięte
  w Optimie znikają, wypłaty i elementy listy są wymieniane w całości.
- **R21.** Wypłata wskazuje pracownika hr.web.app po jego id zapisanym w Optimie (`PRA_ImportRowId`), inaczej po
  akronimie w spółce; nierozpoznana zostaje z pustym pracownikiem (nie odrzuca importu).
- **R22.** Podgląd list płac spółki za miesiąc (Kadry, Administrator) — listy, wypłaty (brutto, do wypłaty) i elementy;
  każdy rozpoznany pracownik dostaje wpis audytu odczytu (`optima_payroll`). Dane są tylko do odczytu i niezależne
  od list płac hr.web.app (009).

## Przypadki brzegowe

- Agent nie odpowiada po `claim` → operacja zostaje `claimed`; operator zleca ponowną próbę (R9), a spóźniony wynik
  starej próby jest ignorowany (R8).
- Dwa składniki tego samego rodzaju na jednej umowie w jednym miesiącu (zmiana wartości w połowie miesiąca) dają
  dwie pozycje, każdą z pełną kwotą — proporcje liczy Optima albo operator poprawia w Optimie.
- Pracownik usunięty z hr.web.app po zleceniu `AddEmployees` znika z danych operacji.

## Poza zakresem

Ekrany frontu (osobne zadanie), aktualizacja pracownika istniejącego w Optimie, korekty wyeksportowanych pozycji,
porównanie list płac Optimy z listami hr.web.app (po 009), wiele organizacji na jednej instalacji.
