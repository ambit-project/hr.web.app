--liquibase formatted sql

--changeset hr:20261113-33
--comment: Parametry listy płac (plan 009) i wartości 2027. Dane produkcyjne, nie deweloperskie. Parametry zależne od
--comment: płacy minimalnej (podstawa chorobowego, próg obniżenia PPK, kwoty wolne od potrąceń) liczone tym samym
--comment: sposobem co wartości 2026 (sprawdzone: 4 806,00 zł → 3 605,85 / 3 501,73 zł); płaca minimalna 2027 i limit
--comment: 30-krotności 2027 według planu 009 — do potwierdzenia aktem przed pierwszą listą 2027 [?].

-- Wartości 2026 bez końca okresu zamykane przed wartościami 2027 (okresy jednego kodu się nie nakładają).
UPDATE platform.legal_parameters
SET valid_to = DATE '2026-12-31'
WHERE valid_from = DATE '2026-01-01' AND valid_to IS NULL
  AND code IN ('placa.minimalna', 'placa.minimalna.godzinowa', 'zus.limit.roczny', 'ppk.prog.obnizenia',
               'potracenia.kwota.wolna.100', 'potracenia.kwota.wolna.100.ppk');

INSERT INTO platform.legal_parameters (code, value, valid_from, valid_to, source) VALUES
('chorobowe.podstawa.minimalna', 4147.10, '2026-01-01', '2026-12-31',
 'Ustawa zasiłkowa, art. 45 ust. 1: płaca minimalna 4 806,00 zł − 13,71%'),
('chorobowe.podstawa.minimalna', 4271.36, '2027-01-01', NULL,
 'Ustawa zasiłkowa, art. 45 ust. 1: płaca minimalna 4 950,00 zł − 13,71% (wyliczone) [?]'),
('chorobowe.pracodawca.dni', 33, '2026-01-01', NULL,
 'Kodeks pracy, art. 92 § 1 pkt 1, t.j. Dz.U. 2026 poz. 1245: wynagrodzenie chorobowe do 33 dni w roku'),
('chorobowe.pracodawca.dni.50', 14, '2026-01-01', NULL,
 'Kodeks pracy, art. 92 § 1 pkt 1: 14 dni po roku, w którym pracownik ukończył 50 lat'),
('urlop.ekwiwalent.wspolczynnik', 20.92, '2026-01-01', '2026-12-31',
 'Rozp. urlopowe Dz.U. 1997 nr 2 poz. 14, § 19: (365 − 104 soboty i niedziele − 8 świąt w dni robocze − 2 święta w soboty) ÷ 12 = 251 ÷ 12 [?] zaokrąglenie'),
('urlop.ekwiwalent.wspolczynnik', 20.92, '2027-01-01', '2027-12-31',
 'Rozp. urlopowe § 19: 251 dni pracy w 2027 (wyliczone ze świąt 2027) ÷ 12 [?] zaokrąglenie'),
('pit.niepobieranie.limit', 30000.00, '2026-01-01', NULL,
 'Ustawa o PIT, art. 31c, t.j. Dz.U. 2026 poz. 592: dochód u płatnika, do którego płatnik nie pobiera zaliczek'),
('placa.minimalna', 4950.00, '2027-01-01', NULL, 'Plan 009: płaca minimalna 2027 [?] — potwierdzić rozporządzeniem RM'),
('placa.minimalna.godzinowa', 32.30, '2027-01-01', NULL,
 'Plan 009: minimalna stawka godzinowa 2027 [?] — potwierdzić rozporządzeniem RM'),
('zus.limit.roczny', 300990.00, '2027-01-01', NULL, 'Plan 009: 30-krotność 2027 [?] — potwierdzić obwieszczeniem MRPiPS'),
('ppk.prog.obnizenia', 5940.00, '2027-01-01', NULL, 'Ustawa o PPK, art. 27 ust. 2; 1,2 × płaca minimalna 2027 (wyliczone)'),
('potracenia.kwota.wolna.100', 3703.93, '2027-01-01', NULL,
 'Kodeks pracy, art. 87^1 i 91; płaca minimalna 2027 netto bez PPK (wyliczone jak 2026)'),
('potracenia.kwota.wolna.100.ppk', 3595.93, '2027-01-01', NULL,
 'Kodeks pracy, art. 87^1 i 91; płaca minimalna 2027 netto z PPK 2% (wyliczone jak 2026)');
