--liquibase formatted sql

--changeset hr:20261113-31 context:dev
--comment: PPK spółki „Demo” (fikcyjna instytucja, wpłata dodatkowa pracodawcy 0%), „Anna Testowa” — uczestnik od
--comment: 2026-04-10 ze stawkami 2% / 0%, „Piotr Kowalski” — uczestnik od 2021-06-10 z rezygnacją złożoną 2026-06-20
--comment: (R16 specyfikacji 008). Okresy stawek wpisane wprost (rutyny modułów powstają po danych). Dane fikcyjne.
INSERT INTO ppk.company_settings (company_id, valid_from, institution_name, agreement_number, agreement_date,
                                  employer_additional_rate)
SELECT c.id, DATE '2021-05-01', 'Fikcyjne TFI S.A. (dev)', 'PPK/DEV/0001', DATE '2021-04-15', 0
FROM platform.companies c
WHERE c.short_name = 'Demo';

INSERT INTO ppk.participations (employee_id, enrolled_at, institution_member_number, initial_basic_rate,
                                initial_additional_rate)
SELECT e.id, v.enrolled_at, v.member_number, 2, 0
FROM employees.employees e
JOIN platform.companies c ON c.id = e.company_id
JOIN (VALUES ('1', DATE '2026-04-10', 'DEV-PPK-0001'), ('2', DATE '2021-06-10', 'DEV-PPK-0002'))
    AS v (number, enrolled_at, member_number) ON v.number = e.number
WHERE c.short_name = 'Demo';

INSERT INTO ppk.rate_periods (participation_id, valid_from, employee_basic_rate, employee_additional_rate)
SELECT p.id, p.enrolled_at, p.initial_basic_rate, p.initial_additional_rate
FROM ppk.participations p
JOIN employees.employees e ON e.id = p.employee_id
JOIN platform.companies c ON c.id = e.company_id
WHERE c.short_name = 'Demo';

INSERT INTO ppk.declarations (employee_id, kind, submitted_at, effective_from)
SELECT e.id, 'resignation', DATE '2026-06-20', DATE '2026-06-01'
FROM employees.employees e JOIN platform.companies c ON c.id = e.company_id
WHERE c.short_name = 'Demo' AND e.number = '2';
