--liquibase formatted sql

--changeset hr:20261113-21 context:dev
--comment: 20 fikcyjnych kartotek w spółce „Demo” z adresem i umową (umowy o pracę, zlecenia, dzieła; część zakończona),
--comment: żeby listy, filtry i statusy miały na czym działać. Bez PESEL (szyfrowanie wymaga klucza z API — ADR-0009).
WITH people (n, first_name, last_name, birth_date, street, postal_code, city, kind, contract_type, start_date,
             end_date, fte_num, fte_den, amount) AS (
    VALUES
        (2, 'Piotr', 'Kowalski', DATE '1985-02-11', 'ul. Polna 4', '00-950', 'Warszawa', 'employment', 'indefinite', DATE '2021-03-01', NULL::date, 1, 1, 8200.00),
        (3, 'Katarzyna', 'Nowak', DATE '1990-07-23', 'ul. Kwiatowa 12/3', '30-001', 'Kraków', 'employment', 'indefinite', DATE '2019-09-01', NULL, 1, 1, 9600.00),
        (4, 'Tomasz', 'Wiśniewski', DATE '1978-11-02', 'ul. Długa 7', '80-001', 'Gdańsk', 'employment', 'fixed_term', DATE '2026-01-01', DATE '2026-12-31', 1, 1, 6500.00),
        (5, 'Agnieszka', 'Wójcik', DATE '1995-05-30', 'ul. Słoneczna 3', '50-001', 'Wrocław', 'employment', 'indefinite', DATE '2022-06-01', NULL, 1, 2, 3500.00),
        (6, 'Marcin', 'Kowalczyk', DATE '1982-01-15', 'ul. Leśna 21', '60-001', 'Poznań', 'employment', 'indefinite', DATE '2020-01-02', NULL, 1, 1, 12500.00),
        (7, 'Magdalena', 'Kamińska', DATE '2002-08-19', 'ul. Ogrodowa 5/10', '90-001', 'Łódź', 'employment', 'trial', DATE '2026-10-01', DATE '2026-12-31', 1, 1, 4806.00),
        (8, 'Paweł', 'Lewandowski', DATE '1970-03-08', 'ul. Szkolna 9', '20-001', 'Lublin', 'employment', 'indefinite', DATE '2018-04-01', NULL, 1, 1, 14200.00),
        (9, 'Joanna', 'Zielińska', DATE '1988-12-01', 'ul. Lipowa 14', '15-001', 'Białystok', 'employment', 'fixed_term', DATE '2024-01-01', DATE '2025-12-31', 1, 1, 7100.00),
        (10, 'Michał', 'Szymański', DATE '1993-09-27', 'ul. Brzozowa 2', '70-001', 'Szczecin', 'employment', 'indefinite', DATE '2023-02-01', NULL, 3, 4, 5400.00),
        (11, 'Ewa', 'Woźniak', DATE '1986-04-04', 'ul. Klonowa 8/1', '40-001', 'Katowice', 'employment', 'indefinite', DATE '2021-11-15', NULL, 1, 1, 10300.00),
        (12, 'Krzysztof', 'Dąbrowski', DATE '1975-06-17', 'ul. Parkowa 11', '25-001', 'Kielce', 'employment', 'indefinite', DATE '2017-05-01', NULL, 1, 1, 11800.00),
        (13, 'Monika', 'Kozłowska', DATE '1999-10-09', 'ul. Polna 30', '35-001', 'Rzeszów', 'employment', 'fixed_term', DATE '2025-07-01', DATE '2027-06-30', 1, 1, 5900.00),
        (14, 'Grzegorz', 'Jankowski', DATE '1968-01-25', 'ul. Wiejska 6', '10-001', 'Olsztyn', 'employment', 'indefinite', DATE '2016-09-01', NULL, 1, 1, 9000.00),
        (15, 'Aleksandra', 'Mazur', DATE '1991-03-14', 'ul. Krótka 1', '45-001', 'Opole', 'employment', 'fixed_term', DATE '2022-03-01', DATE '2026-05-31', 1, 1, 6800.00),
        (16, 'Jakub', 'Krawczyk', DATE '2004-02-28', 'ul. Akacjowa 19', '85-001', 'Bydgoszcz', 'mandate', NULL, DATE '2026-09-01', DATE '2027-02-28', NULL, NULL, 35.00),
        (17, 'Natalia', 'Piotrowska', DATE '2001-07-07', 'ul. Topolowa 4/6', '87-100', 'Toruń', 'mandate', NULL, DATE '2026-03-01', DATE '2026-12-31', NULL, NULL, 42.50),
        (18, 'Adam', 'Grabowski', DATE '1980-05-21', 'ul. Cicha 3', '65-001', 'Zielona Góra', 'mandate', NULL, DATE '2025-01-01', DATE '2025-06-30', NULL, NULL, 60.00),
        (19, 'Zofia', 'Nowakowska', DATE '1997-11-30', 'ul. Wesoła 15', '26-600', 'Radom', 'mandate', NULL, DATE '2026-11-01', DATE '2027-04-30', NULL, NULL, 31.40),
        (20, 'Łukasz', 'Pawłowski', DATE '1984-08-12', 'ul. Zielona 27', '42-200', 'Częstochowa', 'specific_work', NULL, DATE '2026-10-15', DATE '2026-12-15', NULL, NULL, 7500.00),
        (21, 'Weronika', 'Michalska', DATE '1992-09-03', 'ul. Miodowa 2', '33-100', 'Tarnów', 'specific_work', NULL, DATE '2026-12-01', DATE '2027-01-31', NULL, NULL, 3200.00)
), company AS (
    SELECT c.id, c.tax_office_id FROM platform.companies c WHERE c.short_name = 'Demo'
), new_employees AS (
    INSERT INTO employees.employees (company_id, number, first_name, last_name, birth_date, tax_office_id, email)
    SELECT company.id, p.n::text, p.first_name, p.last_name, p.birth_date, company.tax_office_id,
           'pracownik' || p.n || '@dev.example'
    FROM people p CROSS JOIN company
    RETURNING id, number, company_id
), new_addresses AS (
    INSERT INTO employees.addresses (employee_id, kind, street, postal_code, city, valid_from)
    SELECT e.id, 'residence', p.street, p.postal_code, p.city, DATE '2015-01-01'
    FROM new_employees e JOIN people p ON p.n::text = e.number
    RETURNING id
)
INSERT INTO employment.employments (employee_id, company_id, number, kind, contract_type, signed_at, start_date,
                                    end_date)
SELECT e.id, e.company_id, 'DEV-' || e.number, p.kind, p.contract_type, p.start_date - 7, p.start_date, p.end_date
FROM new_employees e JOIN people p ON p.n::text = e.number;

-- Warunki: pierwsza wersja od początku umowy, pola zależnie od rodzaju (004 R10–R12).
INSERT INTO employment.terms (employment_id, valid_from, position_id, fte_numerator, fte_denominator, pay_basis,
                              base_amount, zus_title_code_id, zus_pension_code_id, zus_disability_code_id,
                              insurances, cost_kind)
SELECT m.id, m.start_date,
       CASE WHEN m.kind = 'employment'
            THEN (SELECT p.id FROM dictionaries.positions p WHERE lower(p.name) = lower('Specjalista (dev)')) END,
       x.fte_num, x.fte_den,
       CASE m.kind WHEN 'employment' THEN 'monthly' WHEN 'mandate' THEN 'hourly' ELSE 'lumpSum' END,
       x.amount,
       CASE m.kind
           WHEN 'employment' THEN (SELECT z.id FROM dictionaries.zus_codes z WHERE z.kind = 'title' AND z.code = '0110')
           WHEN 'mandate' THEN (SELECT z.id FROM dictionaries.zus_codes z WHERE z.kind = 'title' AND z.code = '0411')
       END,
       CASE WHEN m.kind <> 'specific_work'
            THEN (SELECT z.id FROM dictionaries.zus_codes z WHERE z.kind = 'pension' AND z.code = '0') END,
       CASE WHEN m.kind <> 'specific_work'
            THEN (SELECT z.id FROM dictionaries.zus_codes z WHERE z.kind = 'disability' AND z.code = '0') END,
       CASE m.kind
           WHEN 'employment' THEN ARRAY['pension', 'disability', 'sickness', 'accident', 'health', 'fp', 'fgsp']
           -- Zlecenie: student do 26 lat bez składek, pozostali — jedyny tytuł.
           WHEN 'mandate' THEN CASE WHEN m.number IN ('DEV-16', 'DEV-17') THEN ARRAY[]::text[]
                                    ELSE ARRAY['pension', 'disability', 'accident', 'health'] END
           ELSE ARRAY[]::text[]
       END,
       CASE m.kind WHEN 'employment' THEN 'standard' ELSE 'flat20' END
FROM employment.employments m
JOIN (VALUES ('DEV-2', 1, 1, 8200.00), ('DEV-3', 1, 1, 9600.00), ('DEV-4', 1, 1, 6500.00), ('DEV-5', 1, 2, 3500.00),
             ('DEV-6', 1, 1, 12500.00), ('DEV-7', 1, 1, 4806.00), ('DEV-8', 1, 1, 14200.00), ('DEV-9', 1, 1, 7100.00),
             ('DEV-10', 3, 4, 5400.00), ('DEV-11', 1, 1, 10300.00), ('DEV-12', 1, 1, 11800.00),
             ('DEV-13', 1, 1, 5900.00), ('DEV-14', 1, 1, 9000.00), ('DEV-15', 1, 1, 6800.00),
             ('DEV-16', NULL, NULL, 35.00), ('DEV-17', NULL, NULL, 42.50), ('DEV-18', NULL, NULL, 60.00),
             ('DEV-19', NULL, NULL, 31.40), ('DEV-20', NULL, NULL, 7500.00), ('DEV-21', NULL, NULL, 3200.00))
    AS x (number, fte_num, fte_den, amount) ON x.number = m.number
JOIN platform.companies c ON c.id = m.company_id AND c.short_name = 'Demo';
