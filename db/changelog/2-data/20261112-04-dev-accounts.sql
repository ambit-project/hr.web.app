--liquibase formatted sql

--changeset hr:20261112-04 context:dev
--comment: Konta deweloperskie bez hasła, tylko do logowania jednym kliknięciem (Development). Produkcja: --contexts=main.
INSERT INTO platform.users (login, email, display_name, is_dev_account)
VALUES ('admin', 'admin@dev.example', 'Administrator (dev)', true),
       ('kadry', 'kadry@dev.example', 'Kadry (dev)', true),
       ('pracownik', 'pracownik@dev.example', 'Pracownik (dev)', true);

INSERT INTO platform.user_roles (user_id, role)
SELECT id, CASE login WHEN 'admin' THEN 'administrator' ELSE login END
FROM platform.users
WHERE is_dev_account;
