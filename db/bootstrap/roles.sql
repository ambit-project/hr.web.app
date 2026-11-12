-- Zakłada bazę i role aplikacji. Uruchamia superużytkownik klastra (raz, idempotentnie):
--   psql -v db=hr -v prefix=hr -v migrator_password=... -v api_password=... -f roles.sql
-- Role:
--   <prefix>_owner     NOLOGIN, właściciel bazy, schematów, tabel i rutyn
--   <prefix>_migrator  LOGIN, uruchamia Liquibase; w sesji działa jako <prefix>_owner
--   <prefix>_api       LOGIN, tylko EXECUTE na rutynach <moduł>_app (bez praw do tabel)
\set ON_ERROR_STOP on
\set owner :prefix '_owner'
\set migrator :prefix '_migrator'
\set api :prefix '_api'

SELECT format('CREATE ROLE %I NOLOGIN', :'owner')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = :'owner') \gexec

SELECT format('CREATE ROLE %I LOGIN', :'migrator')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = :'migrator') \gexec

SELECT format('CREATE ROLE %I LOGIN', :'api')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = :'api') \gexec

SELECT format('ALTER ROLE %I PASSWORD %L', :'migrator', :'migrator_password') \gexec
SELECT format('ALTER ROLE %I PASSWORD %L', :'api', :'api_password') \gexec
SELECT format('GRANT %I TO %I', :'owner', :'migrator') \gexec

SELECT format('CREATE DATABASE %I OWNER %I', :'db', :'owner')
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = :'db') \gexec

SELECT format('REVOKE ALL ON DATABASE %I FROM PUBLIC', :'db') \gexec
SELECT format('GRANT CONNECT ON DATABASE %I TO %I, %I', :'db', :'migrator', :'api') \gexec
-- Wszystko, co utworzy migrator, należy do właściciela.
SELECT format('ALTER ROLE %I IN DATABASE %I SET role = %I', :'migrator', :'db', :'owner') \gexec
