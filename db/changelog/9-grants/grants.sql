--liquibase formatted sql

--changeset hr:grants-api runAlways:true runOnChange:true splitStatements:false
--comment: Rola API: tylko USAGE na schematach <moduł>_app i EXECUTE na ich rutynach. Żadnych praw do tabel.
GRANT USAGE ON SCHEMA platform_app, dictionaries_app, employees_app, employment_app, tax_declarations_app, pay_components_app, optima_app, time_absence_app, ppk_app, payroll_app, documents_app TO ${api_role};
GRANT EXECUTE ON ALL ROUTINES IN SCHEMA platform_app TO ${api_role};
GRANT EXECUTE ON ALL ROUTINES IN SCHEMA dictionaries_app TO ${api_role};
GRANT EXECUTE ON ALL ROUTINES IN SCHEMA employees_app TO ${api_role};
GRANT EXECUTE ON ALL ROUTINES IN SCHEMA employment_app TO ${api_role};
GRANT EXECUTE ON ALL ROUTINES IN SCHEMA tax_declarations_app TO ${api_role};
GRANT EXECUTE ON ALL ROUTINES IN SCHEMA pay_components_app TO ${api_role};
GRANT EXECUTE ON ALL ROUTINES IN SCHEMA optima_app TO ${api_role};
GRANT EXECUTE ON ALL ROUTINES IN SCHEMA time_absence_app TO ${api_role};
GRANT EXECUTE ON ALL ROUTINES IN SCHEMA ppk_app TO ${api_role};
GRANT EXECUTE ON ALL ROUTINES IN SCHEMA payroll_app TO ${api_role};
GRANT EXECUTE ON ALL ROUTINES IN SCHEMA documents_app TO ${api_role};
