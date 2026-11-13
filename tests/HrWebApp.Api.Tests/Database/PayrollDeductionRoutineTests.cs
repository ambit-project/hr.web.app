using HrWebApp.Api.Tests.Infrastructure;
using Npgsql;
using static HrWebApp.Api.Tests.Database.PayrollKit;

namespace HrWebApp.Api.Tests.Database;

/// <summary>Potrącenia z netto (009 R22, R23) i kwota ściągnięta z zajęcia z zamkniętych list (R25).</summary>
[Collection(DatabaseCollection.Name)]
public sealed class PayrollDeductionRoutineTests(DatabaseFixture database)
{
    private readonly PayrollKit _kit = new(database);

    [Fact]
    public async Task Ewa_bailiff_takes_half_of_net_then_the_rest_and_the_garnishment_is_paid_off()
    {
        var company = await _kit.CompanyAsync();
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
        var bailiff = await _kit.GarnishmentAsync(ewa, "other", D(2026, 9, 15), 6000m);
        var card = await _kit.ComponentTypeAsync("deduction", "fixed", "voluntaryOther");
        var consent = await _kit.ConsentAsync(ewa, card, 120m, D(2026, 10, 1));
        await _kit.ComponentAsync(employment, card, 120m, D(2026, 10, 1), consent: consent);

        var october = await _kit.MonthAsync(company, 10);
        var entry = await _kit.EntryAsync(october, employment);

        Assert.Equal(8128.87m, entry.Net);
        // min(½ × 8 128,87; 8 128,87 − kwota wolna z PPK 3 501,73; 6 000).
        Assert.Equal(4064.43m, entry.Amount("KOMOR"));
        Assert.Equal(3944.44m, entry.Payout);
        Assert.Equal(4064.43m, await CollectedAsync(ewa, bailiff));

        var november = await _kit.MonthAsync(company, 11);
        var second = await _kit.EntryAsync(november, employment);

        Assert.Equal(1935.57m, second.Amount("KOMOR"));
        Assert.Equal(6073.30m, second.Payout);
        Assert.Equal(6000.00m, await CollectedAsync(ewa, bailiff));
        Assert.Equal(
            "paidOff",
            await _kit.AdminAsync<string>(
                "SELECT status FROM pay_components.garnishments WHERE id = $1",
                bailiff
            )
        );
        var version = await _kit.AdminAsync<long>(
            "SELECT xmin::text::bigint FROM pay_components.garnishments WHERE id = $1",
            bailiff
        );
        var delete = await Assert.ThrowsAsync<PostgresException>(() =>
            _kit.KadryAsync<object>(
                "CALL pay_components_app.garnishment_delete($1, $2)",
                bailiff,
                version
            )
        );
        Assert.Equal("U0003", delete.SqlState);
        Assert.Equal("payrollClosed", delete.Hint);
    }

    [Fact]
    public async Task Alimony_goes_first_up_to_three_fifths_and_leaves_nothing_for_other_enforcement()
    {
        var company = await _kit.CompanyAsync();
        var employee = await _kit.EmployeeAsync(company, "Dłużnik", D(1985, 1, 1), "85010112358");
        var employment = await _kit.EmploymentAsync(
            employee,
            "employment",
            D(2026, 1, 1),
            null,
            "monthly",
            6000m,
            EmploymentInsurances
        );
        var other = await _kit.GarnishmentAsync(employee, "other", D(2026, 1, 10), 1000m);
        var alimony = await _kit.GarnishmentAsync(employee, "alimony", D(2026, 3, 10), null, 3000m);

        var october = await _kit.MonthAsync(company, 10, close: false);
        var entry = await _kit.EntryAsync(october, employment);

        Assert.Equal(4120.43m, entry.Net);
        // 3/5 × 4 120,43 = 2 472,25 (do grosza w dół); egzekucja inna mieści się w 3/5 łącznie — nic nie zostaje.
        Assert.Equal(2472.25m, entry.Amount("ALIM"));
        Assert.False(entry.Amounts.ContainsKey("KOMOR"));
        Assert.Equal(1648.18m, entry.Payout);
        Assert.Contains("deductionLimited", entry.Warnings);
        Assert.Equal(0m, await CollectedAsync(employee, alimony));
        Assert.Equal(0m, await CollectedAsync(employee, other));
    }

    private async Task<decimal> CollectedAsync(Guid employee, Guid garnishment) =>
        await _kit.KadryAsync<decimal>(
            "SELECT collected_amount FROM pay_components_app.garnishment_list($1) WHERE id = $2",
            employee,
            garnishment
        );
}
