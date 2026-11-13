--liquibase formatted sql

--changeset hr:20261113-20 context:dev
--comment: Premia i karta sportowa oraz zgoda i potrącenie „Anny Testowej” w spółce „Demo” (R25 specyfikacji 006).
--comment: Dane fikcyjne.
INSERT INTO pay_components.component_types (code, name, kind, calculation, in_zus_base, in_health_base, taxable,
                                            in_sickness_base, deduction_category)
VALUES ('PREMIA', 'Premia uznaniowa', 'addition', 'fixed', true, true, true, true, NULL),
       ('KARTA', 'Karta sportowa (udział pracownika)', 'deduction', 'fixed', NULL, NULL, NULL, NULL, 'voluntaryOther');

INSERT INTO pay_components.consents (employee_id, component_type_id, monthly_amount, valid_from, signed_at)
SELECT e.id, t.id, 120.00, DATE '2026-01-01', DATE '2025-12-20'
FROM employees.employees e
JOIN platform.companies c ON c.id = e.company_id
JOIN pay_components.component_types t ON t.code = 'KARTA'
WHERE c.short_name = 'Demo' AND e.number = '1';

INSERT INTO pay_components.employment_components (employment_id, component_type_id, value, valid_from, consent_id)
SELECT m.id, d.component_type_id, 120.00, DATE '2026-01-01', d.id
FROM pay_components.consents d
JOIN employment.employments m ON m.employee_id = d.employee_id AND m.number = '1'
JOIN pay_components.component_types t ON t.id = d.component_type_id AND t.code = 'KARTA';
