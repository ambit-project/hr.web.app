# Specyfikacje (SDD)

Każdy moduł z [PRODUCT.md](../../PRODUCT.md) ma katalog `NNN-<moduł>/` z trzema plikami:

| Plik | Co | Bramka |
|---|---|---|
| `spec.md` | co i dlaczego: wymagania numerowane (`R1`…), przypadki brzegowe, poza zakresem, podstawa prawna | zatwierdza właściciel |
| `plan.md` | jak: tabele, rutyny, endpointy, ekrany, konfiguracja, decyzje (ADR) | zatwierdza właściciel |
| `tasks.md` | kroki implementacji z testami i stanem (`[ ]` / `[x]`) | prowadzi wykonawca |

Model danych: [docs/domain](../domain/README.md). Wymaganie bez testu w `tasks.md` nie jest skończone.

| Nr | Moduł | Stan |
|---|---|---|
| [001](001-platform/spec.md) | Platforma: logowanie, role, spółki, parametry prawne, audyt | część 1 zrobiona (R1–R24, R23 od 003); część 2: OIDC |
| [002](002-dictionaries/spec.md) | Słowniki: stanowiska, kody ZUS, urzędy skarbowe | zrobione (R1–R12) |
| [003](003-employees/spec.md) | Pracownicy: kartoteka, adresy, rachunki, szyfrowanie, profile | zrobione (R1–R15) |
| [004](004-employment/spec.md) | Zatrudnienie: umowy, wersje warunków, zbieg, okres przechowywania | zrobione (R1–R21) |
| [005](005-tax-declarations/spec.md) | Oświadczenia podatkowe: PIT-2, ulgi, koszty, wnioski o zaliczki | zrobione (R1–R16) |
| [006](006-pay-components/spec.md) | Składniki płacowe, zgody na potrącenia, zajęcia wynagrodzenia | zrobione (R1–R25) |
| [007](007-time-and-absence/spec.md) | Czas pracy i nieobecności: kalendarz, nieobecności, limit urlopu, ewidencja godzin | specyfikacja do zatwierdzenia |
