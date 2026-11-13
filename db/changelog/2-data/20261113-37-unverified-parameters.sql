--liquibase formatted sql

--changeset hr:20261113-37
--comment: Zasada „czego nie wiemy, tego nie liczymy”. Parametry 2027 bez źródła usunięte: podstawa minimalna
--comment: chorobowego (sposób zaokrąglenia 13,71% nieustalony) i kwoty wolne od potrąceń (nieogłoszone) — brak
--comment: parametru zatrzymuje naliczenie (U0006). Płaca minimalna 2027, 30-krotność 2027 i cykl ponownego zapisu PPK
--comment: 2027 potwierdzone (źródła w kolumnie source).
--comment: Współczynnik ekwiwalentu zastąpiony liczbą dni pracy roku: rozp. urlopowe § 19 ust. 2 dzieli ją przez 12
--comment: i nie mówi o zaokrągleniu, więc liczymy bez zaokrąglenia.
DELETE FROM platform.legal_parameters
WHERE valid_from = DATE '2027-01-01'
  AND code IN ('chorobowe.podstawa.minimalna', 'potracenia.kwota.wolna.100', 'potracenia.kwota.wolna.100.ppk');

UPDATE platform.legal_parameters
SET source = 'Rozporządzenie RM w sprawie wysokości minimalnego wynagrodzenia za pracę oraz minimalnej stawki '
    || 'godzinowej w 2027 r., Dz.U. 2026 poz. 1213: 4 950 zł'
WHERE code = 'placa.minimalna' AND valid_from = DATE '2027-01-01';
UPDATE platform.legal_parameters
SET source = 'Rozporządzenie RM, Dz.U. 2026 poz. 1213: minimalna stawka godzinowa 32,30 zł od 1.01.2027'
WHERE code = 'placa.minimalna.godzinowa' AND valid_from = DATE '2027-01-01';
UPDATE platform.legal_parameters
SET source = 'Obwieszczenie MRPiPS, M.P. 2026 poz. 1098: ograniczenie rocznej podstawy składek emerytalnej i rentowych '
    || 'w 2027 r. 300 990 zł (30 × prognozowane przeciętne wynagrodzenie 10 033 zł)'
WHERE code = 'zus.limit.roczny' AND valid_from = DATE '2027-01-01';
UPDATE platform.legal_parameters
SET source = 'Ustawa o PPK, art. 27 ust. 2: 1,2 × płaca minimalna 2027 (4 950 zł, Dz.U. 2026 poz. 1213)'
WHERE code = 'ppk.prog.obnizenia' AND valid_from = DATE '2027-01-01';

UPDATE platform.legal_parameters
SET source = 'Ustawa o PPK, art. 23 ust. 5–6, t.j. Dz.U. 2026 poz. 192: co 4 lata od cyklu 2023; mojeppk.pl (PFR): '
    || 'informacja do końca lutego 2027, wpłaty od 1.04.2027'
WHERE code IN ('ppk.ponowny.zapis.informacja', 'ppk.ponowny.zapis.wznowienie') AND valid_from = DATE '2027-01-01';

DELETE FROM platform.legal_parameters WHERE code = 'urlop.ekwiwalent.wspolczynnik';
INSERT INTO platform.legal_parameters (code, value, valid_from, valid_to, source) VALUES
('urlop.ekwiwalent.dni', 251, '2026-01-01', '2026-12-31',
 'Rozp. urlopowe Dz.U. 1997 nr 2 poz. 14, § 19 ust. 2 (brzmienie Dz.U. 2003 nr 230 poz. 2290): 365 − 52 niedziele '
 || '− 10 świąt poza niedzielą − 52 dni wolne pięciodniowego tygodnia = 251; współczynnik = 251 ÷ 12 bez zaokrąglenia'),
('urlop.ekwiwalent.dni', 251, '2027-01-01', '2027-12-31',
 'Rozp. urlopowe § 19 ust. 2: 365 − 52 niedziele − 10 świąt poza niedzielą (ustawa o dniach wolnych od pracy) − 52 dni '
 || 'wolne = 251; współczynnik = 251 ÷ 12 bez zaokrąglenia');
