--liquibase formatted sql

--changeset hr:20261113-36
--comment: Data przekazania wpłat PPK listy do instytucji finansowej (009 R19). Wpłata finansowana przez pracodawcę jest
--comment: przychodem pracownika w dniu przekazania (stanowisko MF z 2019 r.), więc podatek liczy lista z wypłatą po tej
--comment: dacie w tym samym miesiącu, a bez niej — PIT-11 (010 R6).
ALTER TABLE payroll.payrolls ADD COLUMN ppk_paid_on date NULL;
ALTER TABLE payroll.payrolls
    ADD CONSTRAINT payrolls_ppk_paid CHECK (ppk_paid_on IS NULL OR (status = 'closed' AND ppk_paid_on >= payment_date));
