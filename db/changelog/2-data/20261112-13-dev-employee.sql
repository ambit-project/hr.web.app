--liquibase formatted sql

--changeset hr:20261112-13 context:dev
--comment: Spółka „Demo” i kartoteka powiązana z kontem deweloperskim „pracownik” (R15 specyfikacji 003). Bez PESEL:
--comment: szyfrowanie wymaga klucza z API, którego Liquibase nie zna (ADR-0009). Dane fikcyjne.
INSERT INTO platform.companies (short_name, name, nip, regon, street, postal_code, city, tax_office_id)
SELECT 'Demo', 'Spółka Demo sp. z o.o. (dev)', '0000000000', '000000000', 'ul. Przykładowa 1', '00-001', 'Przykładowo',
       (SELECT id FROM dictionaries.tax_offices WHERE code = '1438');

INSERT INTO employees.employees (company_id, number, first_name, last_name, birth_date, tax_office_id, email)
SELECT c.id, '1', 'Anna', 'Testowa', DATE '1988-04-12', c.tax_office_id, 'pracownik@dev.example'
FROM platform.companies c
WHERE c.short_name = 'Demo';

INSERT INTO employees.addresses (employee_id, kind, street, postal_code, city, valid_from)
SELECT e.id, 'residence', 'ul. Lipowa 1/2', '00-001', 'Przykładowo', DATE '2020-01-01'
FROM employees.employees e JOIN platform.companies c ON c.id = e.company_id
WHERE c.short_name = 'Demo';

INSERT INTO platform.user_employee_links (user_id, employee_id, company_id)
SELECT u.id, e.id, e.company_id
FROM platform.users u, employees.employees e JOIN platform.companies c ON c.id = e.company_id
WHERE u.login = 'pracownik' AND c.short_name = 'Demo';
