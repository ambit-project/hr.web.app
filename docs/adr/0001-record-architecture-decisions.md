# ADR-0001: Zapisujemy decyzje architektoniczne

- **Status:** Accepted
- **Data:** 2026-11-12

## Kontekst

Projekt jest prowadzony w Spec-Driven Development: specyfikacje (`docs/specs/`) i decyzje muszą być w repo, żeby
nowa osoba (albo agent) rozumiała, dlaczego kod wygląda tak, a nie inaczej.

## Decyzja

Każda decyzja architektoniczna to plik `docs/adr/NNNN-tytul.md` (Kontekst, Decyzja, Konsekwencje) i wiersz
w `docs/adr/README.md`. ADR opisuje stan kodu; zmiana decyzji = nowy ADR, który zastępuje stary.
Dokumentacja i ADR po polsku, kod i baza po angielsku.

## Konsekwencje

Przegląd zmiany zaczyna się od sprawdzenia, czy nie łamie istniejącego ADR.
