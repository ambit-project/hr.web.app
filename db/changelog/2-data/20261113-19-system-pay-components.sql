--liquibase formatted sql

--changeset hr:20261113-19
--comment: Składniki systemowe liczone przez listę płac (R5 specyfikacji 006). Cechy według modelu domeny
--comment: (docs/domain/06-pay-components.md); potwierdzenie z przepisami przy specyfikacji 009.
INSERT INTO pay_components.component_types (code, name, kind, calculation, in_zus_base, in_health_base, taxable,
                                            in_sickness_base, deduction_category, is_system)
VALUES
    ('ZASAD', 'Wynagrodzenie zasadnicze', 'addition', 'engine', true, true, true, true, NULL, true),
    -- Wynagrodzenie chorobowe nie jest oskładkowane, ale jest opodatkowane i wchodzi do podstawy zdrowotnej.
    ('CHOR', 'Wynagrodzenie chorobowe', 'addition', 'engine', false, true, true, false, NULL, true),
    ('NADG50', 'Wynagrodzenie za nadgodziny z dodatkiem 50%', 'addition', 'engine', true, true, true, true, NULL, true),
    ('NADG100', 'Wynagrodzenie za nadgodziny z dodatkiem 100%', 'addition', 'engine', true, true, true, true, NULL,
     true),
    ('ZLEC', 'Wynagrodzenie z umowy zlecenia', 'addition', 'engine', true, true, true, true, NULL, true),
    -- Dzieło bez ubezpieczeń (umowa nie z własnym pracownikiem — 004 R5).
    ('DZIELO', 'Wynagrodzenie z umowy o dzieło', 'addition', 'engine', false, false, true, false, NULL, true),
    ('ALIM', 'Potrącenie alimentacyjne', 'deduction', 'engine', NULL, NULL, NULL, NULL, 'alimony', true),
    ('KOMOR', 'Potrącenie z tytułu wykonawczego', 'deduction', 'engine', NULL, NULL, NULL, NULL, 'enforcement', true);
