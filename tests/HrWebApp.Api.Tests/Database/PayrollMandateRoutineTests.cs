using HrWebApp.Api.Tests.Infrastructure;
using static HrWebApp.Api.Tests.Database.PayrollKit;

namespace HrWebApp.Api.Tests.Database;

/// <summary>Zlecenie i dzieło (009 R3, R12, R18).</summary>
[Collection(DatabaseCollection.Name)]
public sealed class PayrollMandateRoutineTests(DatabaseFixture database)
{
    private readonly PayrollKit _kit = new(database);

    [Fact]
    public async Task Marek_hourly_mandate_pays_confirmed_hours_with_20_percent_costs()
    {
        var company = await _kit.CompanyAsync();
        var marek = await _kit.EmployeeAsync(company, "Marek", D(1985, 6, 1), "85060112359");
        var mandate = await _kit.EmploymentAsync(
            marek,
            "mandate",
            D(2026, 10, 1),
            D(2026, 12, 31),
            "hourly",
            40m,
            MandateInsurances,
            "flat20"
        );
        await _kit.MandateHoursAsync(mandate, D(2026, 10, 1), D(2026, 10, 20), 50m);
        await _kit.MandateHoursAsync(mandate, D(2026, 10, 21), D(2026, 10, 25), 30m);
        await _kit.MandateHoursAsync(
            mandate,
            D(2026, 10, 26),
            D(2026, 10, 31),
            10m,
            confirmed: false
        );

        var october = await _kit.MonthAsync(company, 10, "mandate", close: false);
        var entry = await _kit.EntryAsync(october, mandate);

        Assert.Equal(80m, entry.Bases["ZLEC"]);
        Assert.Equal(3200.00m, entry.Amount("ZLEC"));
        Assert.Equal(
            438.72m,
            entry.Amount("ZUS-E-P") + entry.Amount("ZUS-R-P") + entry.Amount("ZUS-C-P")
        );
        Assert.Equal(248.52m, entry.Amount("ZDROW"));
        Assert.Equal(552.26m, entry.Amount("KUP"));
        Assert.Equal(2209m, entry.Amount("PODST-PIT"));
        Assert.Equal(265m, entry.Amount("PIT"));
        Assert.Equal(2247.76m, entry.Net);
        Assert.Equal(53.44m, entry.Amount("ZUS-W-PR"));
        Assert.Contains("unconfirmedHours", entry.Warnings);
    }

    [Fact]
    public async Task Small_mandate_up_to_200_is_flat_taxed_unless_the_person_is_an_employee()
    {
        var company = await _kit.CompanyAsync();
        var student = await _kit.EmployeeAsync(company, "Student", D(1995, 1, 1));
        var small = await _kit.EmploymentAsync(
            student,
            "mandate",
            D(2026, 10, 1),
            D(2026, 10, 31),
            "lumpSum",
            150m,
            NoInsurances,
            "flat20"
        );
        var worker = await _kit.EmployeeAsync(company, "Pracownik", D(1990, 1, 1));
        await _kit.EmploymentAsync(
            worker,
            "employment",
            D(2026, 1, 1),
            null,
            "monthly",
            6000m,
            EmploymentInsurances
        );
        var workerMandate = await _kit.EmploymentAsync(
            worker,
            "mandate",
            D(2026, 10, 1),
            D(2026, 10, 31),
            "lumpSum",
            150m,
            NoInsurances,
            "flat20"
        );

        var october = await _kit.MonthAsync(company, 10, "mandate", close: false);
        var flat = await _kit.EntryAsync(october, small);
        var scale = await _kit.EntryAsync(october, workerMandate);

        Assert.Equal(150.00m, flat.Amount("ZLEC"));
        Assert.Equal(18m, flat.Amount("PIT-RYCZ"));
        Assert.False(flat.Amounts.ContainsKey("PIT"));
        Assert.Equal(132.00m, flat.Net);
        // Własny pracownik: koszty 20% = 30, podstawa 120, zaliczka 14,40 → 14.
        Assert.False(scale.Amounts.ContainsKey("PIT-RYCZ"));
        Assert.Equal(30.00m, scale.Amount("KUP"));
        Assert.Equal(14m, scale.Amount("PIT"));
    }

    [Fact]
    public async Task Specific_work_is_paid_in_the_month_it_ends_with_author_costs()
    {
        var company = await _kit.CompanyAsync();
        var anna = await _kit.EmployeeAsync(company, "Anna", D(1990, 5, 10));
        var work = await _kit.EmploymentAsync(
            anna,
            "specific_work",
            D(2026, 11, 20),
            D(2026, 12, 18),
            "lumpSum",
            2000m,
            NoInsurances,
            "author50"
        );

        var november = await _kit.MonthAsync(company, 11, "specificWork", close: false);
        var december = await _kit.MonthAsync(company, 12, "specificWork");
        var entry = await _kit.EntryAsync(december, work);

        Assert.False(await _kit.HasEntryAsync(november, work));
        Assert.Equal(2000.00m, entry.Amount("DZIELO"));
        Assert.False(entry.Amounts.ContainsKey("ZUS-E-P"));
        Assert.False(entry.Amounts.ContainsKey("ZDROW"));
        Assert.Equal(1000.00m, entry.Amount("KUP50"));
        Assert.Equal(120m, entry.Amount("PIT"));
        Assert.Equal(1880.00m, entry.Net);
        var authorCosts = await _kit.KadryAsync<decimal>(
            "SELECT author_costs FROM payroll_app.year_totals($1, 2026)",
            anna
        );
        Assert.Equal(1000.00m, authorCosts);
    }
}
