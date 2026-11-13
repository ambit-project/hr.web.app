using System.Net;
using System.Text.Json;
using HrWebApp.Api.Tests.Database;
using HrWebApp.Api.Tests.Infrastructure;
using static HrWebApp.Api.Tests.Database.PayrollKit;

namespace HrWebApp.Api.Tests.Features.Payroll;

[Collection(DatabaseCollection.Name)]
public sealed class PayrollHttpTests(DatabaseFixture database)
{
    private readonly PayrollKit _kit = new(database);

    [Fact]
    public async Task Kadry_create_calculate_read_and_close_a_list_then_its_period_is_locked()
    {
        using var kadry = await database.Api.ClientAsAsync("kadry");
        var company = await _kit.CompanyAsync();
        var employee = await _kit.EmployeeAsync(company, "Lista", D(1990, 1, 1), "90010112345");
        var employment = await _kit.EmploymentAsync(
            employee,
            "employment",
            D(2026, 1, 1),
            null,
            "monthly",
            7500m,
            EmploymentInsurances
        );

        var settings = await kadry.PostAsJsonAsync(
            $"/api/companies/{company}/payroll-settings",
            new
            {
                validFrom = "2026-01-01",
                accidentRate = 0.0167m,
                paysSicknessAllowance = false,
            }
        );
        var badRate = await kadry.PostAsJsonAsync(
            $"/api/companies/{company}/payroll-settings",
            new
            {
                validFrom = "2026-02-01",
                accidentRate = 0.25m,
                paysSicknessAllowance = false,
            }
        );
        Assert.Equal(HttpStatusCode.OK, settings.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badRate.StatusCode);
        Assert.Contains("accidentRate", await badRate.Content.ReadAsStringAsync());
        var periods = await kadry.GetFromJsonAsync<JsonElement[]>(
            $"/api/companies/{company}/payroll-settings"
        );
        Assert.Equal(0.0167m, Assert.Single(periods!).GetProperty("accidentRate").GetDecimal());

        var created = await kadry.PostAsJsonAsync(
            $"/api/companies/{company}/payrolls",
            new
            {
                kind = "employment",
                period = "2026-11-01",
                paymentDate = "2026-11-30",
            }
        );
        var duplicate = await kadry.PostAsJsonAsync(
            $"/api/companies/{company}/payrolls",
            new
            {
                kind = "employment",
                period = "2026-11-15",
                paymentDate = "2026-11-30",
            }
        );
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Contains("duplicate", await duplicate.Content.ReadAsStringAsync());
        var payroll = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();

        var members = await kadry.GetFromJsonAsync<JsonElement[]>(
            $"/api/payrolls/{payroll}/members"
        );
        var member = Assert.Single(members!);
        Assert.Equal(employment, member.GetProperty("employmentId").GetGuid());
        Assert.False(member.GetProperty("excluded").GetBoolean());
        var excluded = await kadry.PutAsJsonAsync(
            $"/api/payrolls/{payroll}/members",
            new { employmentIds = new[] { employment }, excluded = true }
        );
        var foreign = await kadry.PutAsJsonAsync(
            $"/api/payrolls/{payroll}/members",
            new { employmentIds = new[] { Guid.NewGuid() }, excluded = true }
        );
        Assert.Equal(HttpStatusCode.NoContent, excluded.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
        Assert.Contains("employmentIds", await foreign.Content.ReadAsStringAsync());
        var withExclusion = await kadry.GetFromJsonAsync<JsonElement>($"/api/payrolls/{payroll}");
        Assert.Equal(1, withExclusion.GetProperty("excludedCount").GetInt32());
        var restored = await kadry.PutAsJsonAsync(
            $"/api/payrolls/{payroll}/members",
            new
            {
                rowVersion = withExclusion.GetProperty("rowVersion").GetInt64(),
                employmentIds = new[] { employment },
                excluded = false,
            }
        );
        Assert.Equal(HttpStatusCode.NoContent, restored.StatusCode);

        var early = await kadry.PostAsJsonAsync(
            $"/api/payrolls/{payroll}/close",
            new { rowVersion = (long?)null }
        );
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        Assert.Contains("notCalculated", await early.Content.ReadAsStringAsync());
        var calculated = await kadry.PostAsJsonAsync(
            $"/api/payrolls/{payroll}/calculate",
            new { rowVersion = (long?)null }
        );
        Assert.Equal(HttpStatusCode.NoContent, calculated.StatusCode);

        var summary = await kadry.GetFromJsonAsync<JsonElement>($"/api/payrolls/{payroll}");
        Assert.Equal("calculated", summary.GetProperty("status").GetString());
        Assert.Equal("2026/11/UP", summary.GetProperty("number").GetString());
        Assert.Equal(1, summary.GetProperty("entryCount").GetInt32());
        Assert.Equal(7500m, summary.GetProperty("gross").GetDecimal());
        var listed = Assert.Single(
            (
                await kadry.GetFromJsonAsync<JsonElement[]>(
                    $"/api/companies/{company}/payrolls?year=2026"
                )
            )!
        );
        Assert.Equal(1, listed.GetProperty("entryCount").GetInt32());

        var entry = Assert.Single(
            (await kadry.GetFromJsonAsync<JsonElement[]>($"/api/payrolls/{payroll}/entries"))!
        );
        Assert.Equal(7500m, entry.GetProperty("gross").GetDecimal());
        Assert.True(entry.GetProperty("employeeContributions").GetDecimal() > 0);
        Assert.Equal(JsonValueKind.Array, entry.GetProperty("warnings").ValueKind);
        var details = await kadry.GetFromJsonAsync<JsonElement>(
            $"/api/payroll-entries/{entry.GetProperty("id").GetGuid()}"
        );
        Assert.Equal(employment, details.GetProperty("employmentId").GetGuid());
        var lines = details.GetProperty("lines").EnumerateArray().ToList();
        var basePay = lines.Single(line => line.GetProperty("code").GetString() == "ZASAD");
        Assert.Equal("income", basePay.GetProperty("side").GetString());
        Assert.Equal(7500m, basePay.GetProperty("amount").GetDecimal());
        var pension = lines.Single(line => line.GetProperty("code").GetString() == "ZUS-E-P");
        // The trace is a JSON object, not a string with JSON inside.
        Assert.Equal(JsonValueKind.Object, pension.GetProperty("trace").ValueKind);
        Assert.Equal(
            JsonValueKind.Object,
            pension
                .GetProperty("trace")
                .GetProperty("params")
                .GetProperty("zus.emerytalna.pracownik")
                .ValueKind
        );

        var stale = await kadry.PostAsJsonAsync(
            $"/api/payrolls/{payroll}/close",
            new { rowVersion = 1L }
        );
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var closed = await kadry.PostAsJsonAsync(
            $"/api/payrolls/{payroll}/close",
            new { rowVersion = summary.GetProperty("rowVersion").GetInt64() }
        );
        Assert.Equal(HttpStatusCode.NoContent, closed.StatusCode);

        var absence = await kadry.PostAsJsonAsync(
            $"/api/employments/{employment}/absences",
            new
            {
                absenceTypeId = "NN",
                dateFrom = "2026-11-16",
                dateTo = "2026-11-16",
            }
        );
        var recalculated = await kadry.PostAsJsonAsync(
            $"/api/payrolls/{payroll}/calculate",
            new { rowVersion = (long?)null }
        );
        var deleted = await kadry.DeleteAsync($"/api/payrolls/{payroll}");
        Assert.Equal(HttpStatusCode.Conflict, absence.StatusCode);
        Assert.Contains("payrollClosed", await absence.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, recalculated.StatusCode);
        Assert.Contains("closed", await recalculated.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, deleted.StatusCode);

        var totals = await kadry.GetFromJsonAsync<JsonElement>(
            $"/api/employees/{employee}/year-totals/2026"
        );
        Assert.Equal(7500m, totals.GetProperty("pensionBase").GetDecimal());

        var correction = await kadry.PostAsJsonAsync(
            $"/api/companies/{company}/payrolls",
            new
            {
                kind = "correction",
                paymentDate = "2026-12-10",
                correctsPayrollId = payroll,
            }
        );
        Assert.Equal(HttpStatusCode.OK, correction.StatusCode);
        var correctionId = (await correction.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        var correctionSummary = await kadry.GetFromJsonAsync<JsonElement>(
            $"/api/payrolls/{correctionId}"
        );
        Assert.Equal("2026/11/UP/K1", correctionSummary.GetProperty("number").GetString());
        Assert.Equal(payroll, correctionSummary.GetProperty("correctsPayrollId").GetGuid());
        var removed = await kadry.DeleteAsync(
            $"/api/payrolls/{correctionId}?rowVersion={correctionSummary.GetProperty("rowVersion").GetInt64()}"
        );
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
    }

    [Fact]
    public async Task Pracownik_gets_403_on_payrolls()
    {
        using var pracownik = await database.Api.ClientAsAsync("pracownik");
        var company = await _kit.CompanyAsync();
        var payroll = await _kit.PayrollAsync(
            company,
            "employment",
            D(2026, 10, 1),
            D(2026, 10, 31)
        );
        var employee = await _kit.EmployeeAsync(company, "Obcy", D(1990, 1, 1));

        var list = await pracownik.GetAsync($"/api/companies/{company}/payrolls?year=2026");
        var settings = await pracownik.GetAsync($"/api/companies/{company}/payroll-settings");
        var get = await pracownik.GetAsync($"/api/payrolls/{payroll}");
        var entries = await pracownik.GetAsync($"/api/payrolls/{payroll}/entries");
        var calculate = await pracownik.PostAsJsonAsync(
            $"/api/payrolls/{payroll}/calculate",
            new { rowVersion = (long?)null }
        );
        var create = await pracownik.PostAsJsonAsync(
            $"/api/companies/{company}/payrolls",
            new
            {
                kind = "mandate",
                period = "2026-10-01",
                paymentDate = "2026-10-31",
            }
        );
        var totals = await pracownik.GetAsync($"/api/employees/{employee}/year-totals/2026");

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, settings.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, entries.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, calculate.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, totals.StatusCode);
    }
}
