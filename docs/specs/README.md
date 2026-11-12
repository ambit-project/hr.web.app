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
| [001](001-platform/spec.md) | Platforma: logowanie, role, spółki, parametry prawne, audyt | w realizacji |
