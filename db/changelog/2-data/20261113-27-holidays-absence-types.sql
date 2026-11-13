--liquibase formatted sql

--changeset hr:20261113-27
--comment: Święta ustawowe 2026–2027 (ustawa o dniach wolnych od pracy, t.j. Dz.U. 2025 poz. 296, art. 1 pkt 1; Wigilia
--comment: od 1.02.2025 — Dz.U. 2024 poz. 1965). Święta ruchome: Wielkanoc 5.04.2026 / 28.03.2027, Zielone Świątki
--comment: 24.05.2026 / 16.05.2027, Boże Ciało 4.06.2026 / 27.05.2027. Fakty prawne — kontekst main.
INSERT INTO time_absence.holidays (date, name)
SELECT make_date(y.year, v.month, v.day), v.name
FROM (VALUES (2026), (2027)) AS y (year)
CROSS JOIN (VALUES
    (1, 1, 'Nowy Rok'),
    (1, 6, 'Święto Trzech Króli'),
    (5, 1, 'Święto Państwowe'),
    (5, 3, 'Święto Narodowe Trzeciego Maja'),
    (8, 15, 'Wniebowzięcie Najświętszej Maryi Panny'),
    (11, 1, 'Wszystkich Świętych'),
    (11, 11, 'Narodowe Święto Niepodległości'),
    (12, 24, 'Wigilia Bożego Narodzenia'),
    (12, 25, 'Pierwszy dzień Bożego Narodzenia'),
    (12, 26, 'Drugi dzień Bożego Narodzenia')
) AS v (month, day, name);

INSERT INTO time_absence.holidays (date, name)
VALUES (DATE '2026-04-05', 'Pierwszy dzień Wielkiej Nocy'),
       (DATE '2026-04-06', 'Drugi dzień Wielkiej Nocy'),
       (DATE '2026-05-24', 'Pierwszy dzień Zielonych Świątek'),
       (DATE '2026-06-04', 'Boże Ciało'),
       (DATE '2027-03-28', 'Pierwszy dzień Wielkiej Nocy'),
       (DATE '2027-03-29', 'Drugi dzień Wielkiej Nocy'),
       (DATE '2027-05-16', 'Pierwszy dzień Zielonych Świątek'),
       (DATE '2027-05-27', 'Boże Ciało');

--changeset hr:20261113-27-absence-types
--comment: Rodzaje nieobecności (R6 specyfikacji 007). Limity: UZ 4 dni w roku (KP art. 167²), OD 2 dni (art. 188),
--comment: SW 2 dni (art. 148¹), UOP 5 dni (art. 173¹), UO 2 dni na zdarzenie (rozporządzenie o usprawiedliwianiu
--comment: nieobecności, Dz.U. 2014 poz. 1632, § 15). Limit w dniach; zwolnienia godzinowe poza zakresem 007.
INSERT INTO time_absence.absence_types (code, name, payment, limit_kind, limit_days, sickness, legal_basis, sort_order)
VALUES ('UW', 'Urlop wypoczynkowy', 'leavePay', 'leave', NULL, false, 'KP art. 152–154', 10),
       ('UZ', 'Urlop na żądanie', 'leavePay', 'leave', 4, false, 'KP art. 167²', 20),
       ('UO', 'Urlop okolicznościowy', 'salary', 'event', 2, false, 'Dz.U. 2014 poz. 1632 § 15', 30),
       ('OD', 'Opieka nad dzieckiem do 14 lat', 'salary', 'yearDays', 2, false, 'KP art. 188', 40),
       ('SW', 'Zwolnienie z powodu siły wyższej', 'salaryHalf', 'yearDays', 2, false, 'KP art. 148¹', 50),
       ('UOP', 'Urlop opiekuńczy', 'unpaid', 'yearDays', 5, false, 'KP art. 173¹', 60),
       ('CH', 'Choroba (L4)', 'sickness80', 'none', NULL, true, 'KP art. 92; ustawa zasiłkowa art. 11', 70),
       ('CHW', 'Choroba — wypadek w drodze do lub z pracy', 'sickness100', 'none', NULL, true, 'KP art. 92 § 1 pkt 2',
        80),
       ('CHC', 'Choroba w okresie ciąży', 'sickness100', 'none', NULL, true, 'KP art. 92 § 1 pkt 2', 90),
       ('UB', 'Urlop bezpłatny', 'unpaid', 'none', NULL, false, 'KP art. 174', 100),
       ('NN', 'Nieobecność nieusprawiedliwiona', 'unpaid', 'none', NULL, false, NULL, 110);
