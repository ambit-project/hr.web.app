--liquibase formatted sql

--changeset hr:20261112-01
--comment: Schematy platformy i słowników; rutyny nie są wykonywalne dla PUBLIC.
CREATE SCHEMA platform;
CREATE SCHEMA dictionaries;
CREATE SCHEMA dictionaries_app;
REVOKE ALL ON SCHEMA public FROM PUBLIC;
ALTER DEFAULT PRIVILEGES REVOKE EXECUTE ON ROUTINES FROM PUBLIC;
