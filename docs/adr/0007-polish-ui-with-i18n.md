# ADR-0007: UI po polsku przez i18n, błędy jako kody

- **Status:** Accepted
- **Data:** 2026-11-12

## Kontekst

Produkt działa tylko w Polsce, ale repo jest publiczne i ma być gotowe na drugi język.

## Decyzja

- `i18next` + `react-i18next`, zasoby w `apps/web/src/i18n/pl.ts`, inicjalizacja synchroniczna (ten sam tekst
  w SSR i w przeglądarce). Klucze są typowane (`i18next.d.ts`).
- Formularz trzyma kody błędów, nie zdania: zod i API (`U0001`, `errors`) używają tych samych kodów pól
  (`required`, `maxLength`, `duplicate`), a pole tłumaczy je przez `validation.<kod>`.
- Dokumenty (pasek, PIT, zgody) są po polsku niezależnie od języka UI.

## Konsekwencje

Nowy kod błędu w bazie wymaga wpisu w `db/README.md`, w `forms/validation.ts` i w `pl.ts`.
