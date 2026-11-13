using HrWebApp.Api.Tests.Infrastructure;
using Npgsql;
using static HrWebApp.Api.Tests.Database.PayrollKit;

namespace HrWebApp.Api.Tests.Database;

/// <summary>Lista korygująca (009 R4) i blokada danych zamkniętego okresu poza korektą (R25 [D]).</summary>
[Collection(DatabaseCollection.Name)]
public sealed class PayrollCorrectionRoutineTests(DatabaseFixture database)
{
    private readonly PayrollKit _kit = new(database);

    [Fact]
    public async Task October_correction_paid_in_december_records_differences_with_december_tax()
    {
        var company = await _kit.CompanyAsync();
        var anna = await _kit.EmployeeAsync(company, "Anna", D(1990, 5, 10), "90051012348");
        var employment = await _kit.EmploymentAsync(
            anna,
            "employment",
            D(2026, 1, 1),
            null,
            "monthly",
            7500m,
            EmploymentInsurances
        );
        await _kit.Pit2Async(anna);
        await _kit.PpkAsync(anna, D(2026, 4, 10));
        var bonus = await _kit.ComponentTypeAsync("addition", "fixed");
        var october = await _kit.MonthAsync(company, 10);
        // Grudniowa lista zwykła jest naliczona przed korektą — bierze kwotę zmniejszającą grudnia.
        await _kit.MonthAsync(company, 12, close: false);

        var blocked = await Assert.ThrowsAsync<PostgresException>(() =>
            AddBonusAsync(employment, bonus)
        );
        Assert.Equal("payrollClosed", blocked.Hint);

        var correction = await _kit.PayrollAsync(
            company,
            "correction",
            D(2026, 10, 1),
            D(2026, 12, 10),
            october
        );
        await AddBonusAsync(employment, bonus);
        await _kit.CalculateAsync(correction);
        var entry = await _kit.EntryAsync(correction, employment);

        Assert.Equal(500.00m, entry.Gross);
        Assert.False(entry.Amounts.ContainsKey("ZASAD"));
        Assert.Equal(48.80m, entry.Amount("ZUS-E-P"));
        Assert.Equal(7.50m, entry.Amount("ZUS-R-P"));
        Assert.Equal(12.25m, entry.Amount("ZUS-C-P"));
        Assert.Equal(38.83m, entry.Amount("ZDROW"));
        Assert.Equal(10.00m, entry.Amount("PPK-P"));
        Assert.Equal(7.50m, entry.Amount("PPK-PR"));
        // Bez kosztów (już ujęte) i bez kwoty zmniejszającej (użyta w grudniu): 500 + 7,50 − 68,55 = 438,95.
        Assert.False(entry.Amounts.ContainsKey("KUP"));
        Assert.False(entry.Amounts.ContainsKey("PIT-ZMN"));
        Assert.Equal(439m, entry.Amount("PODST-PIT"));
        Assert.Equal(53m, entry.Amount("PIT"));
        Assert.Equal(329.62m, entry.Net);
        Assert.Equal(32.50m, entry.Amount("ZUS-R-PR"));
        Assert.Equal(8.35m, entry.Amount("ZUS-W-PR"));
        Assert.Equal(12.25m, entry.Amount("FP"));
        Assert.Equal(0.50m, entry.Amount("FGSP"));

        await _kit.CloseAsync(correction);
        var original = await _kit.EntryAsync(october, employment);
        Assert.Equal(5279.29m, original.Net);
        Assert.False(original.Amounts.ContainsKey(await BonusCodeAsync(bonus)));
        var totals = await _kit.KadryAsync<decimal>(
            "SELECT pension_base FROM payroll_app.year_totals($1, 2026)",
            anna
        );
        Assert.Equal(8000m, totals);
        var again = await Assert.ThrowsAsync<PostgresException>(() =>
            _kit.KadryAsync<object>(
                "CALL time_absence_app.absence_create($1, 'UB', DATE '2026-10-20', DATE '2026-10-20', NULL, NULL, NULL)",
                employment
            )
        );
        Assert.Equal("payrollClosed", again.Hint);
        Assert.Equal(
            "2026/10/UP/K1",
            await _kit.AdminAsync<string>(
                "SELECT number FROM payroll.payrolls WHERE id = $1",
                correction
            )
        );
    }

    [Fact]
    public async Task Correction_without_changes_has_no_entries_and_needs_a_closed_regular_list()
    {
        var company = await _kit.CompanyAsync();
        var employee = await _kit.EmployeeAsync(company, "Bez", D(1990, 1, 1), "90010112345");
        var employment = await _kit.EmploymentAsync(
            employee,
            "employment",
            D(2026, 1, 1),
            null,
            "monthly",
            6000m,
            EmploymentInsurances
        );
        var open = await _kit.MonthAsync(company, 9, close: false);
        var notClosed = await Assert.ThrowsAsync<PostgresException>(() =>
            _kit.PayrollAsync(company, "correction", D(2026, 9, 1), D(2026, 10, 10), open)
        );
        await _kit.CloseAsync(open);

        var correction = await _kit.PayrollAsync(
            company,
            "correction",
            D(2026, 9, 1),
            D(2026, 10, 10),
            open
        );
        await _kit.CalculateAsync(correction);

        Assert.Equal("""{"correctsPayrollId": ["invalid"]}""", notClosed.Hint);
        Assert.False(await _kit.HasEntryAsync(correction, employment));
        var ofCorrection = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await _kit.CloseAsync(correction);
            await _kit.PayrollAsync(
                company,
                "correction",
                D(2026, 9, 1),
                D(2026, 10, 10),
                correction
            );
        });
        Assert.Equal("""{"correctsPayrollId": ["invalid"]}""", ofCorrection.Hint);
    }

    private async Task AddBonusAsync(Guid employment, Guid bonus) =>
        await _kit.KadryAsync<object>(
            "CALL pay_components_app.employment_component_add($1, $2, 500, DATE '2026-10-01', DATE '2026-10-31', NULL, NULL)",
            employment,
            bonus
        );

    private async Task<string> BonusCodeAsync(Guid bonus) =>
        (
            await _kit.AdminAsync<string>(
                "SELECT code FROM pay_components.component_types WHERE id = $1",
                bonus
            )
        )!;
}
