--liquibase formatted sql

--changeset hr:20261113-30
--comment: Daty cyklu ponownego zapisu do PPK (R12 specyfikacji 008; ustawa o PPK art. 23 ust. 5–6, t.j. Dz.U. 2026
--comment: poz. 192): informacja do ostatniego dnia lutego, wznowienie wpłat od 1 kwietnia, co 4 lata od 2023.
--comment: Wartość = data RRRRMMDD (tabela parametrów trzyma liczby), okres = rok cyklu; rok bez wpisu nie jest rokiem
--comment: cyklu. Cykl 2027 z rachunku „co 4 lata” — komunikat PFR niepotwierdzony. Dane produkcyjne.
INSERT INTO platform.legal_parameters (code, value, valid_from, valid_to, source) VALUES
('ppk.ponowny.zapis.informacja', 20230228, '2023-01-01', '2023-12-31', 'Ustawa o PPK, art. 23 ust. 5; pierwszy cykl 2023'),
('ppk.ponowny.zapis.wznowienie', 20230401, '2023-01-01', '2023-12-31', 'Ustawa o PPK, art. 23 ust. 5; pierwszy cykl 2023'),
('ppk.ponowny.zapis.informacja', 20270228, '2027-01-01', '2027-12-31', 'Ustawa o PPK, art. 23 ust. 5, t.j. Dz.U. 2026 poz. 192; cykl co 4 lata (komunikat PFR niepotwierdzony)'),
('ppk.ponowny.zapis.wznowienie', 20270401, '2027-01-01', '2027-12-31', 'Ustawa o PPK, art. 23 ust. 5, t.j. Dz.U. 2026 poz. 192; cykl co 4 lata (komunikat PFR niepotwierdzony)');
