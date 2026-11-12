--liquibase formatted sql

--changeset hr:20261112-15 context:dev
--comment: Umowa o pracę kartoteki „Anna Testowa” w spółce „Demo” (R21 specyfikacji 004). Dane fikcyjne.
INSERT INTO dictionaries.positions (name, description)
SELECT 'Specjalista (dev)', 'Stanowisko danych deweloperskich'
WHERE NOT EXISTS (SELECT FROM dictionaries.positions p WHERE lower(p.name) = lower('Specjalista (dev)'));

INSERT INTO employment.employments (employee_id, company_id, number, kind, contract_type, signed_at, start_date)
SELECT e.id, e.company_id, '1', 'employment', 'indefinite', DATE '2025-12-15', DATE '2026-01-01'
FROM employees.employees e JOIN platform.companies c ON c.id = e.company_id
WHERE c.short_name = 'Demo' AND e.number = '1';

INSERT INTO employment.terms (employment_id, valid_from, position_id, fte_numerator, fte_denominator, pay_basis,
                              base_amount, zus_title_code_id, zus_pension_code_id, zus_disability_code_id,
                              insurances, cost_kind)
SELECT m.id, m.start_date,
       (SELECT p.id FROM dictionaries.positions p WHERE lower(p.name) = lower('Specjalista (dev)')),
       1, 1, 'monthly', 6000.00,
       (SELECT z.id FROM dictionaries.zus_codes z WHERE z.kind = 'title' AND z.code = '0110'),
       (SELECT z.id FROM dictionaries.zus_codes z WHERE z.kind = 'pension' AND z.code = '0'),
       (SELECT z.id FROM dictionaries.zus_codes z WHERE z.kind = 'disability' AND z.code = '0'),
       ARRAY['pension', 'disability', 'sickness', 'accident', 'health', 'fp', 'fgsp'], 'standard'
FROM employment.employments m JOIN platform.companies c ON c.id = m.company_id
WHERE c.short_name = 'Demo' AND m.number = '1';
