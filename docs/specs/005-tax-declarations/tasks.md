# 005 Oświadczenia podatkowe — zadania

[Specyfikacja](spec.md) · [Plan](plan.md)

- [x] T1. Baza: tabela z `EXCLUDE` tego samego rodzaju, funkcje wewnętrzne (koniec naturalny, dozwolone miesiące,
      wygasanie po końcu umowy, status, `in_force`), rutyny listy, zapisu, zakończenia i usunięcia, dane deweloperskie,
      uprawnienia. Testy rutyn.
- [x] T2. API: endpointy oświadczeń, migawka OpenAPI. Testy HTTP.
- [x] T3. Front: zakładka „Oświadczenia” (lista, „tylko obowiązujące”, szuflada nowego wpisu z miesiącami do wyboru,
      zakończenie i cofnięcie, usunięcie), sekcja w „Moich dokumentach”, etykieta w audycie. Przy okazji: błąd z serwera
      przy polu znika przy następnej zmianie (wcześniej blokował ponowne zapisanie formularza).
- [x] T4. Dokumentacja: kody pól, PRODUCT, mapa wymaganie → test.

## Mapa wymaganie → test

Testy rutyn: `tests/HrWebApp.Api.Tests/Database/TaxDeclarationRoutineTests.cs`; HTTP:
`tests/HrWebApp.Api.Tests/Features/TaxDeclarations/TaxDeclarationHttpTests.cs`.

| R | Test |
|---|---|
| R1, R2 | `Details_depend_on_kind` |
| R3 | `Valid_from_is_the_month_of_submission_or_the_next_one`; front: `types.test.ts` (`allowedMonths`) |
| R4 | `No_advances_lasts_one_year_and_return_relief_ends_with_its_last_year` |
| R5, R6 | `Same_kind_does_not_overlap_and_ending_allows_a_new_entry`, `Ending_is_checked_can_be_undone_and_needs_current_row_version` |
| R7, R8, R11 | `Declarations_lapse_at_the_end_of_continuous_contracts_except_waivers` |
| R9, R12 | `Ending_is_checked_…` (wersja wiersza, usunięcie, 4 wpisy audytu), `TaxDeclarationHttpTests.Kadry_create_list_end_and_delete_a_declaration` |
| R10 | `TaxDeclarationHttpTests.Kadry_create_…` (`onlyActive`) |
| R13, R14 | `Pracownik_reads_only_own_declarations_and_cannot_write`, `TaxDeclarationHttpTests.Pracownik_reads_own_declarations_and_gets_403_on_write_and_foreign_record` |
| R15 | brak osobnej reguły (okres przechowywania kartoteki z 004 R17) |
| R16 | ręcznie: „Anna Testowa” (Demo) ma PIT-2 1/12 od 01.2026, widoczny też w „Moich dokumentach” konta `pracownik` |

Sprawdzone w przeglądarce: Pracownik widzi swój PIT-2 w „Moich dokumentach”. Kadry: domyślny miesiąc „od” po
wpisaniu daty złożenia (10.11.2026 → 12.2026), drugi PIT-2 odrzucony (`overlap` przy polu), po zmianie rodzaju na
wyższą stawkę zapis przechodzi. Zakończenie z wycofaniem sprzed złożenia pokazuje błędy pól, a poprawne (05.10.2026 →
od 11.2026) daje okres 01.2026–10.2026 i status „zakończone”. Działa też cofnięcie zakończenia i usunięcie wpisu.
