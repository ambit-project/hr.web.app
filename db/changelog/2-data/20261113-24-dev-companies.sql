--liquibase formatted sql

--changeset hr:20261113-24 context:dev
--comment: Pięć dodatkowych fikcyjnych spółek grupy (razem z „Demo” sześć), z poprawnymi sumami kontrolnymi NIP i REGON,
--comment: do pracy z wieloma płatnikami i kopiowania kartotek między spółkami. Dane fikcyjne.
INSERT INTO platform.companies (short_name, name, nip, regon, street, postal_code, city, tax_office_id)
SELECT v.short_name, v.name, v.nip, v.regon, v.street, v.postal_code, v.city,
       (SELECT t.id FROM dictionaries.tax_offices t WHERE t.code = v.tax_office)
FROM (VALUES
    ('Budownictwo', 'Budownictwo Przykładowe sp. z o.o. (dev)', '3025071825', '206081944', 'ul. Budowlana 10', '00-120', 'Warszawa', '1438'),
    ('Logistyka', 'Logistyka Testowa S.A. (dev)', '1470486408', '865422151', 'ul. Magazynowa 5', '05-800', 'Pruszków', '1471'),
    ('IT Serwis', 'IT Serwis Przykład sp. z o.o. (dev)', '8646580879', '758714400', 'ul. Programistów 3', '30-415', 'Kraków', '1206'),
    ('Handel', 'Handel Hurtowy Testowy sp. j. (dev)', '1222175284', '465336518', 'ul. Kupiecka 21', '61-001', 'Poznań', '0271'),
    ('Usługi', 'Usługi Wspólne Grupy sp. z o.o. (dev)', '2829386917', '309890262', 'ul. Wspólna 7', '50-001', 'Wrocław', '1438')
) AS v (short_name, name, nip, regon, street, postal_code, city, tax_office)
WHERE NOT EXISTS (SELECT FROM platform.companies c WHERE c.nip = v.nip);
