--liquibase formatted sql

--changeset hr:20261113-35 context:dev
--comment: Spółka deweloperska „Demo” dostaje fikcyjny NIP z poprawną sumą kontrolną — zera nie przechodziły wzorca
--comment: NIP w schemacie PIT-11(29), więc XML Demo był niepoprawny. Dane fikcyjne.
UPDATE platform.companies SET nip = '4775945803' WHERE short_name = 'Demo' AND nip = '0000000000';
