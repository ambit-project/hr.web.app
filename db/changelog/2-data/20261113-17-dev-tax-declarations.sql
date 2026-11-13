--liquibase formatted sql

--changeset hr:20261113-17 context:dev
--comment: PIT-2 kartoteki „Anna Testowa” w spółce „Demo” (R16 specyfikacji 005). Dane fikcyjne.
INSERT INTO tax_declarations.declarations (employee_id, kind, share, submitted_at, valid_from)
SELECT e.id, 'pit2', '1/12', DATE '2025-12-15', DATE '2026-01-01'
FROM employees.employees e JOIN platform.companies c ON c.id = e.company_id
WHERE c.short_name = 'Demo' AND e.number = '1';
