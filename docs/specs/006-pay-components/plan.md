# 006 Składniki płacowe, zgody, zajęcia — plan

[Specyfikacja](spec.md)

## Decyzje (właściciel: „wybierz najlepsze rozwiązanie”)

- **Rodzaj i kod składnika są niezmienne** po założeniu; sposób liczenia można zmienić, dopóki składnik nie jest
  przypisany (`componentInUse`). Powód: zmiana rodzaju dodatek ↔ potrącenie zmieniłaby znaczenie historii.
- **Kategorie systemowe** `alimony` i `enforcement` mają tylko składniki `ALIM` i `KOMOR` (constraint) — Administrator
  ich nie wybiera, bo potrącenia z zajęć liczy lista płac z rejestru zajęć.
- **Zgoda: podpisanie to zmiana niepodpisanej zgody** (ustawienie `signedAt`); po podpisaniu treść jest niezmienna,
  zostaje zakończenie datą. Jedna rutyna `consent_update` działa tylko na niepodpisanej (`U0003` `signed`).
- **FK przypisania → zgoda bez kaskady** (sprawdzane na końcu instrukcji), więc usunięcie kartoteki usuwa umowy,
  przypisania i zgody razem, a usunięcie samej zgody w użyciu blokuje rutyna (`consentInUse`).
- **Zajęcie wynagrodzenia w jednym dialogu** (9 pól w trzech wierszach) — poniżej progu kreatora.
- **Audyt odczytów**: nowe zasoby `employment_components` (kwoty na umowie) i `garnishment` (zajęcia).

## Baza

Schematy `pay_components` (tabele, funkcje wewnętrzne) i `pay_components_app` (kontrakt).

| Tabela | Kolumny (skrót) |
|---|---|
| `component_types` | id, code (UK), name, kind, calculation, in_zus_base, in_health_base, taxable, in_sickness_base, deduction_category, is_system, is_active |
| `consents` | id, employee_id (FK, kaskada), component_type_id, monthly_amount, valid_from, valid_to, signed_at |
| `employment_components` | id, employment_id (FK, kaskada), component_type_id, value, valid_from, valid_to, consent_id (FK) — `EXCLUDE` nakładania tego samego składnika na umowie |
| `garnishments` | id, employee_id (FK, kaskada), kind, authority_kind, authority_name, case_number, received_at, monthly_amount, total_amount, status, status_date, note — UK (kartoteka, organ, sygnatura) bez wielkości liter |

Rutyny `pay_components_app`: `component_type_list/create/update`; `employment_component_list/add/end/delete`;
`consent_list/create/update/end/delete`; `garnishment_list/create/update/set_status/delete`. Funkcje wewnętrzne:
walidacja wartości składnika, pokrycie przez zgodę (`consent_covers`), statusy.

Dane: składniki systemowe w `main` (R5), `PREMIA`, `KARTA` i dane Anny w `dev` (R25).

## API

- `/api/pay-component-types` (GET z `includeInactive`, POST), `/api/pay-component-types/{id}` (PUT).
- `/api/employments/{id}/components` (GET, POST), `/api/employment-components/{id}/end` (POST),
  `/api/employment-components/{id}` (DELETE z `rowVersion`).
- `/api/employees/{id}/deduction-consents` (GET, POST), `/api/deduction-consents/{id}` (PUT, DELETE),
  `/api/deduction-consents/{id}/end` (POST).
- `/api/employees/{id}/garnishments` (GET, POST), `/api/garnishments/{id}` (PUT, DELETE),
  `/api/garnishments/{id}/status` (POST).

## Front

- Menu „Słowniki → Składniki płacowe”: lista, dialog składnika (Administrator; Kadry tylko czytają).
- Strona umowy: sekcja „Składniki” (lista, dialog przypisania z wyborem zgody, zakończenie, usunięcie).
- Kartoteka: zakładka „Potrącenia” z sekcjami „Zgody na potrącenia” i „Zajęcia wynagrodzenia” (dialogi).
- „Moje dokumenty”: zgody pracownika.

## Testy

Baza: słownik (kod, cechy zależnie od rodzaju, systemowe tylko do odczytu, `componentInUse`), przypisanie (okres,
nakładanie, kara tylko na umowie o pracę, zgoda: brak, za mała, nie pokrywa, niepodpisana), zgoda (podpisanie,
niezmienność po podpisaniu, zakończenie w użyciu, usunięcie w użyciu), zajęcie (kwoty zależnie od rodzaju, organ
wierzyciel tylko dla alimentów, duplikat, status), audyt odczytów, role. HTTP: przepływy i 403 Pracownika.
