--liquibase formatted sql

--changeset hr:grants-api runAlways:true splitStatements:false
--comment: Rola API: tylko USAGE na schematach <moduł>_app i EXECUTE na ich rutynach. Żadnych praw do tabel.
GRANT USAGE ON SCHEMA dictionaries_app TO ${api_role};
GRANT EXECUTE ON ALL ROUTINES IN SCHEMA dictionaries_app TO ${api_role};
