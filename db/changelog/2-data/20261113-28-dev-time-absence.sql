--liquibase formatted sql

--changeset hr:20261113-28 context:dev
--comment: Kalendarze 2026 spółek deweloperskich z dniami wolnymi za święta w sobotę (15.08 → piątek 14.08, 26.12 →
--comment: czwartek 31.12) oraz limit urlopu 2026 (26 dni), urlop 3 dni i choroba 5 dni „Anny Testowej” w spółce „Demo”
--comment: (R20 specyfikacji 007). Generowanie jak w time_absence_app.calendar_generate. Dane fikcyjne.
INSERT INTO time_absence.calendar_days (company_id, date, kind, hours)
SELECT c.id, d::date,
       CASE
           WHEN h.date IS NOT NULL THEN 'holiday'
           WHEN d::date IN (DATE '2026-08-14', DATE '2026-12-31') THEN 'designatedOff'
           WHEN extract(isodow FROM d) IN (6, 7) THEN 'dayOff'
           ELSE 'workday'
       END,
       CASE
           WHEN h.date IS NULL AND d::date NOT IN (DATE '2026-08-14', DATE '2026-12-31')
               AND extract(isodow FROM d) NOT IN (6, 7) THEN 8
           ELSE 0
       END
FROM platform.companies c
CROSS JOIN generate_series(DATE '2026-01-01', DATE '2026-12-31', interval '1 day') d
LEFT JOIN time_absence.holidays h ON h.date = d::date
WHERE c.name LIKE '%(dev)'
  AND NOT EXISTS (
      SELECT FROM time_absence.calendar_days x
      WHERE x.company_id = c.id AND x.date BETWEEN DATE '2026-01-01' AND DATE '2026-12-31'
  );

INSERT INTO time_absence.leave_entitlements (employment_id, year, base_days, first_job)
SELECT m.id, 2026, 26, false
FROM employment.employments m JOIN platform.companies c ON c.id = m.company_id
WHERE c.short_name = 'Demo' AND m.number = '1';

INSERT INTO time_absence.absences (employment_id, absence_type_code, date_from, date_to, certificate_number, note)
SELECT m.id, v.code, v.date_from, v.date_to, v.certificate, v.note
FROM employment.employments m
JOIN platform.companies c ON c.id = m.company_id
CROSS JOIN (VALUES
    ('CH', DATE '2026-03-09', DATE '2026-03-13', 'ZZ0000001', 'Zaświadczenie fikcyjne (dev)'),
    ('UW', DATE '2026-07-06', DATE '2026-07-08', NULL, NULL)
) AS v (code, date_from, date_to, certificate, note)
WHERE c.short_name = 'Demo' AND m.number = '1';
