using HrWebApp.Api.Tests.Infrastructure;
using static HrWebApp.Api.Tests.Database.PayrollKit;

namespace HrWebApp.Api.Tests.Database;

/// <summary>
/// Naliczenie umowy o pracę (009 R5–R11, R13–R17, R19–R21) na przypadkach z docs/domain/09-payroll.md, gdzie zgodne ze
/// specyfikacją; różnice — docs/specs/009-payroll/tasks.md.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PayrollEmploymentRoutineTests(DatabaseFixture database)
{
    private readonly PayrollKit _kit = new(database);

    [Fact]
    public async Task Anna_october_full_month_with_ppk_and_pit2()
    {
        var company = await _kit.CompanyAsync();
        var (_, employment) = await AnnaAsync(company);

        var october = await _kit.MonthAsync(company, 10, close: false);
        var entry = await _kit.EntryAsync(october, employment);

        Assert.Equal(7500.00m, entry.Amount("ZASAD"));
        Assert.Equal(732.00m, entry.Amount("ZUS-E-P"));
        Assert.Equal(112.50m, entry.Amount("ZUS-R-P"));
        Assert.Equal(183.75m, entry.Amount("ZUS-C-P"));
        Assert.Equal(582.46m, entry.Amount("ZDROW"));
        Assert.Equal(6471.75m, entry.Bases["ZDROW"]);
        Assert.Equal(150.00m, entry.Amount("PPK-P"));
        Assert.Equal(112.50m, entry.Amount("PPK-PR"));
        Assert.Equal(250.00m, entry.Amount("KUP"));
        // R19: wpłata pracodawcy PPK tego miesiąca w przychodzie — 7 500 + 112,50 − 1 028,25 − 250 = 6 334,25.
        Assert.Equal(6334m, entry.Amount("PODST-PIT"));
        Assert.Equal(300.00m, entry.Amount("PIT-ZMN"));
        Assert.Equal(460m, entry.Amount("PIT"));
        Assert.Equal(7500.00m, entry.Gross);
        Assert.Equal(5279.29m, entry.Net);
        Assert.Equal(5279.29m, entry.Payout);
        // Koszt pracodawcy: emerytalna, rentowa 6,5%, wypadkowa 1,67%, FP + FS 2,45%, FGŚP 0,1%.
        Assert.Equal(732.00m, entry.Amount("ZUS-E-PR"));
        Assert.Equal(487.50m, entry.Amount("ZUS-R-PR"));
        Assert.Equal(125.25m, entry.Amount("ZUS-W-PR"));
        Assert.Equal(183.75m, entry.Amount("FP"));
        Assert.Equal(7.50m, entry.Amount("FGSP"));
    }

    [Fact]
    public async Task Anna_november_sickness_from_ten_closed_months_and_overtime()
    {
        var company = await _kit.CompanyAsync();
        var (_, employment) = await AnnaAsync(company);
        for (var month = 1; month <= 10; month++)
        {
            await _kit.MonthAsync(company, month);
        }

        await _kit.AbsenceAsync(employment, "CH", D(2026, 11, 16), D(2026, 11, 20));
        await _kit.OvertimeAsync(employment, D(2026, 11, 3), 2m, 50);
        await _kit.OvertimeAsync(employment, D(2026, 11, 4), 2m, 50);
        var november = await _kit.MonthAsync(company, 11, close: false);
        var entry = await _kit.EntryAsync(november, employment);

        Assert.Equal(7500.00m, entry.Amount("ZASAD"));
        Assert.Equal(-1250.00m, entry.Amount("ZASAD-CH"));
        // 4 h × 7 500 / 160 h × 1,5.
        Assert.Equal(281.25m, entry.Amount("NADG50"));
        // Podstawa: (9 × (7 000 − 959,70) + (7 500 − 1 028,25)) / 10 = 6 083,45; dzienna 202,78 × 80% = 162,22.
        Assert.Equal(6083.45m, entry.Bases["CHOR"]);
        Assert.Equal(811.10m, entry.Amount("CHOR"));
        Assert.Equal(7342.35m, entry.Gross);
        Assert.Equal(6531.25m, entry.Bases["ZUS-E-P"]);
        Assert.Equal(637.45m, entry.Amount("ZUS-E-P"));
        Assert.Equal(97.97m, entry.Amount("ZUS-R-P"));
        Assert.Equal(160.02m, entry.Amount("ZUS-C-P"));
        Assert.Equal(580.22m, entry.Amount("ZDROW"));
        Assert.Equal(130.63m, entry.Amount("PPK-P"));
        Assert.Equal(97.97m, entry.Amount("PPK-PR"));
        Assert.Equal(6295m, entry.Amount("PODST-PIT"));
        Assert.Equal(455m, entry.Amount("PIT"));
        Assert.Equal(5281.06m, entry.Net);
        Assert.Equal(424.53m, entry.Amount("ZUS-R-PR"));
        Assert.Equal(109.07m, entry.Amount("ZUS-W-PR"));
        Assert.Equal(160.02m, entry.Amount("FP"));
        Assert.Equal(6.53m, entry.Amount("FGSP"));
    }

    [Fact]
    public async Task Piotr_half_time_youth_relief_without_fp_and_tax()
    {
        var company = await _kit.CompanyAsync();
        var piotr = await _kit.EmployeeAsync(company, "Piotr", D(2004, 3, 1), "04230112351");
        var employment = await _kit.EmploymentAsync(
            piotr,
            "employment",
            D(2026, 6, 1),
            null,
            "monthly",
            2600m,
            EmploymentInsurances,
            fteNumerator: 1,
            fteDenominator: 2
        );

        var october = await _kit.MonthAsync(company, 10);
        var entry = await _kit.EntryAsync(october, employment);

        Assert.Equal(
            356.46m,
            entry.Amount("ZUS-E-P") + entry.Amount("ZUS-R-P") + entry.Amount("ZUS-C-P")
        );
        Assert.Equal(201.92m, entry.Amount("ZDROW"));
        Assert.Equal(2600.00m, entry.Amount("ZWOL-MLODZI"));
        Assert.Equal(0m, entry.Amount("PIT"));
        Assert.Equal(2041.62m, entry.Net);
        // FP 0 — podstawa poniżej płacy minimalnej; FGŚP bez progu.
        Assert.False(entry.Amounts.ContainsKey("FP"));
        Assert.Equal(2.60m, entry.Amount("FGSP"));
        Assert.Equal(43.42m, entry.Amount("ZUS-W-PR"));
        Assert.Empty(entry.Warnings);
        var exempt = await _kit.KadryAsync<decimal>(
            "SELECT exempt_income FROM payroll_app.year_totals($1, 2026)",
            piotr
        );
        Assert.Equal(2600.00m, exempt);
    }

    [Fact]
    public async Task Unpaid_leave_force_majeure_and_leave_with_variable_pay_from_closed_months()
    {
        var company = await _kit.CompanyAsync();
        var employee = await _kit.EmployeeAsync(company, "Jan", D(1985, 1, 1), "85010112358");
        var employment = await _kit.EmploymentAsync(
            employee,
            "employment",
            D(2026, 9, 1),
            null,
            "monthly",
            7500m,
            EmploymentInsurances
        );
        await _kit.OvertimeAsync(employment, D(2026, 9, 10), 10m, 50);
        var september = await _kit.MonthAsync(company, 9);
        Assert.Equal(639.20m, (await _kit.EntryAsync(september, employment)).Amount("NADG50"));

        await _kit.AbsenceAsync(employment, "UB", D(2026, 10, 5), D(2026, 10, 5));
        await _kit.AbsenceAsync(employment, "SW", D(2026, 10, 6), D(2026, 10, 6));
        await _kit.AbsenceAsync(employment, "UW", D(2026, 10, 7), D(2026, 10, 8));
        var october = await _kit.MonthAsync(company, 10, close: false);
        var entry = await _kit.EntryAsync(october, employment);

        // 16 h (UB + SW) × 7 500 / 176 h; połowa za SW wraca jako SW50.
        Assert.Equal(-681.82m, entry.Amount("ZASAD-NB"));
        Assert.Equal(170.45m, entry.Amount("SW50"));
        // Zmienne z września: 639,20 / (176 + 10) h = 3,4366 zł/h × 16 h urlopu.
        Assert.Equal(54.99m, entry.Amount("URLOP-ZM"));
        Assert.Equal(7500.00m - 681.82m + 170.45m + 54.99m, entry.Gross);
    }

    [Fact]
    public async Task Sickness_over_33_days_switches_to_allowance_paid_by_zus_or_company()
    {
        var company = await _kit.CompanyAsync();
        var employee = await _kit.EmployeeAsync(company, "Chory", D(1980, 1, 1), "80010112356");
        var employment = await _kit.EmploymentAsync(
            employee,
            "employment",
            D(2026, 1, 1),
            null,
            "monthly",
            6000m,
            EmploymentInsurances
        );
        // 27 dni w styczniu + 20 w lutym: dni 28–33 (1–6.02) płaci pracodawca, od 7.02 zasiłek.
        await _kit.AbsenceAsync(employment, "CH", D(2026, 1, 5), D(2026, 2, 20));

        var february = await _kit.MonthAsync(company, 2, close: false);
        var entry = await _kit.EntryAsync(february, employment);

        // Bez zamkniętych list: 6 000 − 822,60 składek = 5 177,40; dzienna 172,58 × 80% = 138,06.
        Assert.Equal(5177.40m, entry.Bases["CHOR"]);
        Assert.Equal(828.36m, entry.Amount("CHOR"));
        Assert.Equal(1932.84m, entry.Amount("ZAS-ZUS"));
        Assert.False(entry.Amounts.ContainsKey("ZAS-CH"));
        Assert.Equal(-4000.00m, entry.Amount("ZASAD-CH"));
        Assert.Equal(2000.00m + 828.36m, entry.Gross);

        await _kit.KadryAsync<object>(
            "CALL payroll_app.company_settings_set($1, DATE '2026-01-01', 0.0167, true, NULL)",
            company
        );
        Assert.True(
            await _kit.AdminAsync<bool>(
                "SELECT is_stale FROM payroll.payrolls WHERE id = $1",
                february
            )
        );
        await _kit.CalculateAsync(february);
        entry = await _kit.EntryAsync(february, employment);
        Assert.Equal(1932.84m, entry.Amount("ZAS-CH"));
        Assert.Equal(2000.00m + 828.36m + 1932.84m, entry.Gross);
        // Zasiłek bez składek: podstawa ZUS = samo zasadnicze.
        Assert.Equal(2000.00m, entry.Bases["ZUS-E-P"]);
    }

    [Fact]
    public async Task Pension_and_disability_stop_at_the_annual_limit()
    {
        var company = await _kit.CompanyAsync();
        var employee = await _kit.EmployeeAsync(company, "Prezes", D(1975, 1, 1), "75010112353");
        var employment = await _kit.EmploymentAsync(
            employee,
            "employment",
            D(2026, 1, 1),
            null,
            "monthly",
            60000m,
            EmploymentInsurances
        );
        for (var month = 1; month <= 4; month++)
        {
            await _kit.MonthAsync(company, month);
        }

        var may = await _kit.MonthAsync(company, 5);
        var entry = await _kit.EntryAsync(may, employment);
        var june = await _kit.EntryAsync(
            await _kit.MonthAsync(company, 6, close: false),
            employment
        );

        // 282 600 − 4 × 60 000 = 42 600.
        Assert.Equal(42600m, entry.Bases["ZUS-E-P"]);
        Assert.Equal(4157.76m, entry.Amount("ZUS-E-P"));
        Assert.Equal(639.00m, entry.Amount("ZUS-R-P"));
        Assert.Equal(2769.00m, entry.Amount("ZUS-R-PR"));
        Assert.Equal(1470.00m, entry.Amount("ZUS-C-P"));
        Assert.Equal(1470.00m, entry.Amount("FP"));
        Assert.Contains("pensionLimitReached", entry.Warnings);
        Assert.Equal(0m, june.Amount("ZUS-E-P"));
        Assert.Equal(1470.00m, june.Amount("ZUS-C-P"));
        var pensionBase = await _kit.KadryAsync<decimal>(
            "SELECT pension_base FROM payroll_app.year_totals($1, 2026)",
            employee
        );
        Assert.Equal(282600m, pensionBase);
    }

    [Fact]
    public async Task Ewa_december_crosses_the_120000_threshold()
    {
        var company = await _kit.CompanyAsync();
        var (ewa, employment) = await EwaAsync(company);
        for (var month = 1; month <= 11; month++)
        {
            await _kit.MonthAsync(company, month);
        }

        var totals = await _kit.KadryAsync<decimal>(
            "SELECT tax_base_employment FROM payroll_app.year_totals($1, 2026)",
            ewa
        );
        var december = await _kit.MonthAsync(company, 12, close: false);
        var entry = await _kit.EntryAsync(december, employment);

        Assert.Equal(113135m, totals);
        Assert.Equal(10285m, entry.Amount("PODST-PIT"));
        // 12% × 6 865 + 32% × 3 420 − 300 = 1 618,20.
        Assert.Equal(1618m, entry.Amount("PIT"));
        Assert.Equal(7444.87m, entry.Net);
        // Kobieta od 55 lat: bez FP i FGŚP.
        Assert.False(entry.Amounts.ContainsKey("FP"));
        Assert.False(entry.Amounts.ContainsKey("FGSP"));
    }

    [Fact]
    public async Task Leave_equivalent_in_the_month_the_contract_ends()
    {
        var company = await _kit.CompanyAsync();
        var employee = await _kit.EmployeeAsync(company, "Odchodzi", D(1988, 1, 1), "88010112352");
        var employment = await _kit.EmploymentAsync(
            employee,
            "employment",
            D(2026, 1, 1),
            D(2026, 11, 30),
            "monthly",
            6000m,
            EmploymentInsurances
        );
        await _kit.AdminAsync<object>(
            "INSERT INTO time_absence.leave_entitlements (employment_id, year, base_days) VALUES ($1, 2026, 20)",
            employment
        );
        await _kit.AbsenceAsync(employment, "UW", D(2026, 11, 2), D(2026, 11, 3));

        var november = await _kit.MonthAsync(company, 11, close: false);
        var entry = await _kit.EntryAsync(november, employment);

        // Limit 19 dni (11/12 z 20) = 152 h − 16 h = 136 h; 6 000 / 20,92 = 286,81 / 8 = 35,85 zł/h.
        Assert.Equal(136m, entry.Bases["EKWIW"]);
        Assert.Equal(4875.60m, entry.Amount("EKWIW"));
        Assert.Equal(6000.00m, entry.Amount("ZASAD"));
        Assert.Equal(10875.60m, entry.Bases["ZUS-E-P"]);
    }

    [Fact]
    public async Task Sickness_within_a_month_of_the_previous_one_keeps_its_base()
    {
        var company = await _kit.CompanyAsync();
        var employee = await _kit.EmployeeAsync(company, "Nawrót", D(1980, 1, 1), "80010112356");
        var employment = await _kit.EmploymentAsync(
            employee,
            "employment",
            D(2026, 1, 1),
            null,
            "monthly",
            6000m,
            EmploymentInsurances
        );
        await _kit.AbsenceAsync(employment, "CH", D(2026, 1, 12), D(2026, 1, 16));
        await _kit.MonthAsync(company, 1);
        await _kit.TermsAsync(employment, D(2026, 2, 1), 8000m);
        await _kit.AbsenceAsync(employment, "CH", D(2026, 2, 9), D(2026, 2, 10));

        var february = await _kit.EntryAsync(
            await _kit.MonthAsync(company, 2, close: false),
            employment
        );

        // Przerwa krótsza niż miesiąc (art. 43): podstawa ze stycznia (warunki 6 000 − składki), a nie ze stycznia
        // jako zamkniętego miesiąca (5 000 − 685,50).
        Assert.Equal(5177.40m, february.Bases["CHOR"]);
        Assert.Equal(276.12m, february.Amount("CHOR"));
        Assert.Equal(-533.33m, february.Amount("ZASAD-CH"));
    }

    [Fact]
    public async Task Reduced_ppk_rate_is_ignored_above_the_threshold_and_missing_pesel_warns()
    {
        var company = await _kit.CompanyAsync();
        var employee = await _kit.EmployeeAsync(company, "BezPesel", D(1980, 1, 1));
        var employment = await _kit.EmploymentAsync(
            employee,
            "employment",
            D(2026, 1, 1),
            null,
            "monthly",
            6000m,
            EmploymentInsurances
        );
        await _kit.PpkAsync(employee, D(2026, 1, 1), 1m);

        var entry = await _kit.EntryAsync(
            await _kit.MonthAsync(company, 10, close: false),
            employment
        );

        // 6 000 > 1,2 × płaca minimalna (5 767,20): obniżona stawka 1% pominięta — 2%.
        Assert.Equal(120.00m, entry.Amount("PPK-P"));
        Assert.Equal(90.00m, entry.Amount("PPK-PR"));
        Assert.Contains("peselMissing", entry.Warnings);
        Assert.Equal(147.00m, entry.Amount("FP"));
        Assert.Equal(6.00m, entry.Amount("FGSP"));
    }

    [Fact]
    public async Task Youth_relief_waiver_and_higher_rate_declarations_change_the_advance()
    {
        var company = await _kit.CompanyAsync();
        var employee = await _kit.EmployeeAsync(company, "Młody", D(2004, 3, 1), "04230112351");
        var employment = await _kit.EmploymentAsync(
            employee,
            "employment",
            D(2026, 1, 1),
            null,
            "monthly",
            6000m,
            EmploymentInsurances
        );
        await _kit.DeclarationAsync(employee, "youthReliefWaiver", D(2026, 1, 1));
        await _kit.DeclarationAsync(employee, "higherRate", D(2026, 1, 1));

        var entry = await _kit.EntryAsync(
            await _kit.MonthAsync(company, 10, close: false),
            employment
        );

        // Bez zwolnienia; 32% × (6 000 − 822,60 − 250 = 4 927,40 → 4 927) = 1 576,64.
        Assert.False(entry.Amounts.ContainsKey("ZWOL-MLODZI"));
        Assert.Equal(4927m, entry.Amount("PODST-PIT"));
        Assert.Equal(1577m, entry.Amount("PIT"));
    }

    [Fact]
    public async Task Part_month_by_norm_hours_terms_change_by_days_and_percent_component()
    {
        var company = await _kit.CompanyAsync();
        var newcomer = await _kit.EmployeeAsync(company, "Nowy", D(1990, 1, 1), "90010112345");
        var joined = await _kit.EmploymentAsync(
            newcomer,
            "employment",
            D(2026, 10, 15),
            null,
            "monthly",
            7500m,
            EmploymentInsurances
        );
        var raised = await _kit.EmployeeAsync(company, "Podwyżka", D(1990, 1, 1), "90010112352");
        var changed = await _kit.EmploymentAsync(
            raised,
            "employment",
            D(2026, 1, 1),
            null,
            "monthly",
            6000m,
            EmploymentInsurances
        );
        await _kit.TermsAsync(changed, D(2026, 10, 16), 6200m);
        var bonus = await _kit.ComponentTypeAsync("addition", "percentOfBase");
        await _kit.ComponentAsync(changed, bonus, 10m, D(2026, 10, 1));

        var october = await _kit.MonthAsync(company, 10, close: false);
        var part = await _kit.EntryAsync(october, joined);
        var split = await _kit.EntryAsync(october, changed);

        // Od 15.10: 96 h z 176 h wymiaru października.
        Assert.Equal(4090.91m, part.Amount("ZASAD"));
        // 6 000 × 15/31 + 6 200 × 16/31.
        Assert.Equal(6103.23m, split.Amount("ZASAD"));
        Assert.Equal(610.32m, split.Gross - split.Amount("ZASAD"));
    }

    private async Task<(Guid Employee, Guid Employment)> AnnaAsync(Guid company)
    {
        var anna = await _kit.EmployeeAsync(company, "Anna", D(1990, 5, 10), "90051012348");
        var employment = await _kit.EmploymentAsync(
            anna,
            "employment",
            D(2026, 1, 1),
            null,
            "monthly",
            7000m,
            EmploymentInsurances
        );
        await _kit.TermsAsync(employment, D(2026, 10, 1), 7500m);
        await _kit.Pit2Async(anna);
        await _kit.PpkAsync(anna, D(2026, 4, 10));
        return (anna, employment);
    }

    private async Task<(Guid Employee, Guid Employment)> EwaAsync(Guid company)
    {
        var ewa = await _kit.EmployeeAsync(company, "Ewa", D(1970, 2, 2), "70020212344");
        var employment = await _kit.EmploymentAsync(
            ewa,
            "employment",
            D(2026, 1, 1),
            null,
            "monthly",
            12000m,
            EmploymentInsurances
        );
        await _kit.Pit2Async(ewa);
        await _kit.PpkAsync(ewa, D(2026, 1, 1), 2m, 1m);
        return (ewa, employment);
    }
}
