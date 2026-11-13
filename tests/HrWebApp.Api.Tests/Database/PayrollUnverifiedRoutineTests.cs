using HrWebApp.Api.Tests.Infrastructure;
using Npgsql;
using static HrWebApp.Api.Tests.Database.PayrollKit;

namespace HrWebApp.Api.Tests.Database;

/// <summary>
/// Zasada „czego nie wiemy, tego nie liczymy” (009): przypadki bez potwierdzonej reguły dostają ostrzeżenie blokujące
/// zamknięcie listy (U0003 unsupportedCases), a reguły potwierdzone w przepisach — obniżenie zdrowotnej (art. 83),
/// przychód z wpłaty PPK po przekazaniu (R19) — są liczone.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PayrollUnverifiedRoutineTests(DatabaseFixture database)
{
    private readonly PayrollKit _kit = new(database);

    [Fact]
    public async Task Sickness_in_the_waiting_period_is_not_calculated_and_blocks_closing()
    {
        var company = await _kit.CompanyAsync();
        var employee = await _kit.EmployeeAsync(company, "Nowy", D(1980, 1, 1), "80010112356");
        var employment = await _kit.EmploymentAsync(
            employee,
            "employment",
            D(2026, 10, 1),
            null,
            "monthly",
            6000m,
            EmploymentInsurances
        );
        await _kit.AbsenceAsync(employment, "CH", D(2026, 10, 20), D(2026, 10, 22));

        var october = await _kit.MonthAsync(company, 10, close: false);
        var entry = await _kit.EntryAsync(october, employment);

        Assert.Contains("sicknessWaitingPeriod", entry.Warnings);
        Assert.False(entry.Amounts.ContainsKey("CHOR"));
        await AssertCloseBlockedAsync(october);
    }

    [Fact]
    public async Task Sickness_base_without_closed_months_or_needing_supplement_is_not_calculated()
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
        await _kit.MonthAsync(company, 1);
        await _kit.AbsenceAsync(employment, "CH", D(2026, 3, 9), D(2026, 3, 10));

        // Luty bez zamkniętej listy — historia poza systemem.
        var march = await _kit.MonthAsync(company, 3, close: false);
        var entry = await _kit.EntryAsync(march, employment);
        Assert.Contains("sicknessBaseUnknown", entry.Warnings);
        Assert.False(entry.Amounts.ContainsKey("CHOR"));
        await AssertCloseBlockedAsync(march);

        // Luty obniżony urlopem bezpłatnym przy przepracowanej ponad połowie — wymaga uzupełnienia (art. 38 ust. 2 pkt 2).
        await _kit.KadryAsync<object>("CALL payroll_app.payroll_delete($1, NULL)", march);
        await _kit.AbsenceAsync(employment, "UB", D(2026, 2, 2), D(2026, 2, 3));
        await _kit.MonthAsync(company, 2);
        march = await _kit.MonthAsync(company, 3, close: false);
        entry = await _kit.EntryAsync(march, employment);
        Assert.Contains("sicknessBaseSupplement", entry.Warnings);
        await AssertCloseBlockedAsync(march);
    }

    [Fact]
    public async Task Overtime_with_other_components_and_equivalent_with_components_are_not_calculated()
    {
        var company = await _kit.CompanyAsync();
        var employee = await _kit.EmployeeAsync(company, "Dodatek", D(1980, 1, 1), "80010112356");
        var employment = await _kit.EmploymentAsync(
            employee,
            "employment",
            D(2026, 1, 1),
            D(2026, 10, 31),
            "monthly",
            6000m,
            EmploymentInsurances
        );
        await _kit.AdminAsync<object>(
            "INSERT INTO time_absence.leave_entitlements (employment_id, year, base_days) VALUES ($1, 2026, 20)",
            employment
        );
        var bonus = await _kit.ComponentTypeAsync("addition", "fixed");
        await _kit.ComponentAsync(employment, bonus, 300m, D(2026, 10, 1));
        await _kit.OvertimeAsync(employment, D(2026, 10, 6), 2m, 50);

        var october = await _kit.MonthAsync(company, 10, close: false);
        var entry = await _kit.EntryAsync(october, employment);

        Assert.Contains("overtimeNormalPayUnknown", entry.Warnings);
        Assert.False(entry.Amounts.ContainsKey("NADG50"));
        Assert.Contains("equivalentComponentsUnknown", entry.Warnings);
        Assert.False(entry.Amounts.ContainsKey("EKWIW"));
        await AssertCloseBlockedAsync(october);
    }

    [Fact]
    public async Task Health_is_capped_at_the_2021_advance_when_the_case_is_settled_and_blocks_otherwise()
    {
        var company = await _kit.CompanyAsync();
        var small = await _kit.EmployeeAsync(company, "Mała", D(1980, 1, 1), "80010112356");
        var job = await _kit.EmploymentAsync(
            small,
            "employment",
            D(2026, 1, 1),
            null,
            "monthly",
            1000m,
            EmploymentInsurances,
            fteNumerator: 1,
            fteDenominator: 4
        );
        await _kit.Pit2Async(small);
        var shared = await _kit.EmployeeAsync(company, "Dzielona", D(1990, 1, 1), "90010112352");
        var sharedJob = await _kit.EmploymentAsync(
            shared,
            "employment",
            D(2026, 1, 1),
            null,
            "monthly",
            1000m,
            EmploymentInsurances,
            fteNumerator: 1,
            fteDenominator: 4
        );
        await _kit.DeclarationAsync(shared, "pit2", D(2026, 1, 1), "1/24");

        var october = await _kit.MonthAsync(company, 10, close: false);
        var entry = await _kit.EntryAsync(october, job);

        // 1 000 − 137,10 − 250 = 612,90 → 613 × 17% = 104,21 − 43,76 = 60,45 → 60 < 9% × 862,90 = 77,66.
        Assert.Equal(60.00m, entry.Amount("ZDROW"));
        Assert.Empty(entry.Warnings);
        // PIT-2 1/24 — w przepisach z 2021 r. takiego oświadczenia nie było.
        var other = await _kit.EntryAsync(october, sharedJob);
        Assert.Contains("healthCapUnknown", other.Warnings);
        await AssertCloseBlockedAsync(october);
    }

    [Fact]
    public async Task No_advances_month_crossing_30000_is_not_calculated_and_later_months_lose_the_reduction()
    {
        var company = await _kit.CompanyAsync();
        var employee = await _kit.EmployeeAsync(company, "Bez", D(1980, 1, 1), "80010112356");
        var employment = await _kit.EmploymentAsync(
            employee,
            "employment",
            D(2026, 1, 1),
            null,
            "monthly",
            12000m,
            EmploymentInsurances
        );
        await _kit.Pit2Async(employee);
        await _kit.DeclarationAsync(employee, "noAdvances", D(2026, 1, 1));
        await _kit.MonthAsync(company, 1);
        await _kit.MonthAsync(company, 2);

        // 2 × 10 105 = 20 210; marzec przekracza 30 000.
        var march = await _kit.MonthAsync(company, 3, close: false);
        var entry = await _kit.EntryAsync(march, employment);
        Assert.Contains("noAdvancesThresholdCrossed", entry.Warnings);
        Assert.False(entry.Amounts.ContainsKey("PIT"));
        await AssertCloseBlockedAsync(march);
    }

    [Fact]
    public async Task Ppk_payment_date_needs_a_closed_list_and_cannot_change_after_the_income_is_taxed()
    {
        var company = await _kit.CompanyAsync();
        var employee = await _kit.EmployeeAsync(company, "Anna", D(1990, 5, 10), "90051012348");
        var employment = await _kit.EmploymentAsync(
            employee,
            "employment",
            D(2026, 1, 1),
            null,
            "monthly",
            7000m,
            EmploymentInsurances
        );
        await _kit.PpkAsync(employee, D(2026, 4, 10));
        var august = await _kit.MonthAsync(company, 8, close: false);

        var notClosed = await Assert.ThrowsAsync<PostgresException>(() =>
            _kit.PpkPaidAsync(august, D(2026, 9, 10))
        );
        Assert.Equal("payrollNotClosed", notClosed.Hint);
        await _kit.CloseAsync(august);
        var before = await Assert.ThrowsAsync<PostgresException>(() =>
            _kit.PpkPaidAsync(august, D(2026, 8, 20))
        );
        Assert.Equal("U0001", before.SqlState);

        var september = await _kit.MonthAsync(company, 9, close: false);
        await _kit.PpkPaidAsync(august, D(2026, 9, 10));
        Assert.True(
            await _kit.AdminAsync<bool>(
                "SELECT is_stale FROM payroll.payrolls WHERE id = $1",
                september
            )
        );
        await _kit.CalculateAsync(september);
        Assert.Equal(105.00m, (await _kit.EntryAsync(september, employment)).Amount("PPK-PRZ"));
        await _kit.CloseAsync(september);

        var taxed = await Assert.ThrowsAsync<PostgresException>(() =>
            _kit.PpkPaidAsync(august, null)
        );
        Assert.Equal("ppkIncomeTaxed", taxed.Hint);
    }

    private async Task AssertCloseBlockedAsync(Guid payroll)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => _kit.CloseAsync(payroll));
        Assert.Equal("U0003", error.SqlState);
        Assert.Equal("unsupportedCases", error.Hint);
    }
}
