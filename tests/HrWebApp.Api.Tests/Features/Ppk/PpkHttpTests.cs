using System.Net;
using System.Text.Json;
using HrWebApp.Api.Tests.Infrastructure;
using Npgsql;

namespace HrWebApp.Api.Tests.Features.Ppk;

[Collection(DatabaseCollection.Name)]
public sealed class PpkHttpTests(DatabaseFixture database)
{
    [Fact]
    public async Task Kadry_set_up_company_ppk_enroll_and_record_declarations()
    {
        using var kadry = await database.Api.ClientAsAsync("kadry");
        var company = await TestDb.CompanyAsync(database);

        var settings = await kadry.PostAsJsonAsync(
            $"/api/companies/{company}/ppk/settings",
            new
            {
                validFrom = "2026-01-01",
                institutionName = "TFI Testowe",
                agreementNumber = "PPK/1",
                agreementDate = "2025-12-10",
                employerAdditionalRate = 0.5m,
            }
        );
        Assert.Equal(HttpStatusCode.OK, settings.StatusCode);
        var midMonth = await kadry.PostAsJsonAsync(
            $"/api/companies/{company}/ppk/settings",
            new
            {
                validFrom = "2026-02-15",
                institutionName = "TFI Testowe",
                agreementNumber = "PPK/1",
                agreementDate = "2025-12-10",
                employerAdditionalRate = 0.5m,
            }
        );
        Assert.Equal(HttpStatusCode.BadRequest, midMonth.StatusCode);
        Assert.Contains("validFrom", await midMonth.Content.ReadAsStringAsync());
        var periods = await kadry.GetFromJsonAsync<JsonElement[]>(
            $"/api/companies/{company}/ppk/settings"
        );
        Assert.Equal(
            0.5m,
            Assert.Single(periods!).GetProperty("employerAdditionalRate").GetDecimal()
        );

        var employee = await EmployeeAsync(company);
        var candidate = Assert.Single(
            (
                await kadry.GetFromJsonAsync<JsonElement[]>(
                    $"/api/companies/{company}/ppk/candidates?on=2026-04-05"
                )
            )!
        );
        Assert.Equal("toEnroll", candidate.GetProperty("status").GetString());
        Assert.Equal("2026-04-10", candidate.GetProperty("deadline").GetString());

        var enrolled = await kadry.PostAsJsonAsync(
            $"/api/employees/{employee}/ppk",
            new { enrolledAt = "2026-04-10", employeeBasicRate = 2m }
        );
        Assert.Equal(HttpStatusCode.OK, enrolled.StatusCode);
        var twice = await kadry.PostAsJsonAsync(
            $"/api/employees/{employee}/ppk",
            new { enrolledAt = "2026-04-10", employeeBasicRate = 2m }
        );
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
        Assert.Contains("alreadyEnrolled", await twice.Content.ReadAsStringAsync());

        var additional = await kadry.PostAsJsonAsync(
            $"/api/employees/{employee}/ppk/declarations",
            new
            {
                kind = "additionalRate",
                submittedAt = "2026-05-20",
                rate = 1m,
            }
        );
        Assert.Equal(HttpStatusCode.OK, additional.StatusCode);
        var resignation = await kadry.PostAsJsonAsync(
            $"/api/employees/{employee}/ppk/declarations",
            new { kind = "resignation", submittedAt = "2026-11-02" }
        );
        Assert.Equal(HttpStatusCode.OK, resignation.StatusCode);
        var restartless = await kadry.PostAsJsonAsync(
            $"/api/employees/{employee}/ppk/declarations",
            new { kind = "autoRestart", submittedAt = "2026-11-02" }
        );
        Assert.Equal(HttpStatusCode.BadRequest, restartless.StatusCode);

        var ppk = await kadry.GetFromJsonAsync<JsonElement>($"/api/employees/{employee}/ppk");
        Assert.True(ppk.GetProperty("enrolled").GetBoolean());
        Assert.Equal("resigned", ppk.GetProperty("status").GetString());
        Assert.Equal(0m, ppk.GetProperty("employeeBasicRate").GetDecimal());
        Assert.Equal(2, ppk.GetProperty("ratePeriods").GetArrayLength());
        var latest = ppk.GetProperty("declarations")[0];
        Assert.Equal("resignation", latest.GetProperty("kind").GetString());
        Assert.Equal("2026-11-01", latest.GetProperty("effectiveFrom").GetString());

        var deleted = await kadry.DeleteAsync(
            $"/api/ppk/declarations/{latest.GetProperty("id").GetGuid()}?rowVersion={latest.GetProperty("rowVersion").GetInt64()}"
        );
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var after = await kadry.GetFromJsonAsync<JsonElement>($"/api/employees/{employee}/ppk");
        Assert.Equal("active", after.GetProperty("status").GetString());
        Assert.Equal(1m, after.GetProperty("employeeAdditionalRate").GetDecimal());
        Assert.Equal(1.5m, after.GetProperty("employerBasicRate").GetDecimal());
        Assert.Equal(0.5m, after.GetProperty("employerAdditionalRate").GetDecimal());

        var removed = await kadry.DeleteAsync(
            $"/api/employees/{employee}/ppk?rowVersion={after.GetProperty("rowVersion").GetInt64()}"
        );
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        var gone = await kadry.GetFromJsonAsync<JsonElement>($"/api/employees/{employee}/ppk");
        Assert.False(gone.GetProperty("enrolled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, gone.GetProperty("rowVersion").ValueKind);
        Assert.Equal(1, gone.GetProperty("declarations").GetArrayLength());
    }

    [Fact]
    public async Task Kadry_read_reenrollment_cycle_and_need_information_before_apply()
    {
        using var kadry = await database.Api.ClientAsAsync("kadry");
        var company = await TestDb.CompanyAsync(database);

        var cycle = await kadry.GetFromJsonAsync<JsonElement>(
            $"/api/companies/{company}/ppk/reenrollment/2027"
        );
        var notCycle = await kadry.GetAsync($"/api/companies/{company}/ppk/reenrollment/2025");
        var apply = await kadry.PostAsync(
            $"/api/companies/{company}/ppk/reenrollment/2023/apply",
            null
        );
        var informed = await kadry.PostAsJsonAsync(
            $"/api/companies/{company}/ppk/reenrollment/2023/informed",
            new { informedAt = "2023-02-20" }
        );
        var applied = await kadry.PostAsync(
            $"/api/companies/{company}/ppk/reenrollment/2023/apply",
            null
        );

        Assert.Equal("2027-02-28", cycle.GetProperty("informationDeadline").GetString());
        Assert.Equal("2027-04-01", cycle.GetProperty("restartDate").GetString());
        Assert.Equal(0, cycle.GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, notCycle.StatusCode);
        Assert.Contains("notCycleYear", await notCycle.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, apply.StatusCode);
        Assert.Contains("notInformed", await apply.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NoContent, informed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        Assert.Equal(
            0,
            (await applied.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("restarted")
                .GetInt32()
        );
    }

    [Fact]
    public async Task Pracownik_reads_own_ppk_and_gets_403_elsewhere()
    {
        using var pracownik = await database.Api.ClientAsAsync("pracownik");
        var own = await TestDb.AdminScalarAsync<Guid>(
            database,
            "SELECT l.employee_id FROM platform.user_employee_links l JOIN platform.users u ON u.id = l.user_id WHERE u.login = 'pracownik' LIMIT 1"
        );
        var demo = await TestDb.AdminScalarAsync<Guid>(
            database,
            "SELECT id FROM platform.companies WHERE short_name = 'Demo'"
        );
        var foreign = await EmployeeAsync(await TestDb.CompanyAsync(database));

        var mine = await pracownik.GetFromJsonAsync<JsonElement>($"/api/my/{own}/ppk");
        var foreignPpk = await pracownik.GetAsync($"/api/my/{foreign}/ppk");
        var settings = await pracownik.GetAsync($"/api/companies/{demo}/ppk/settings");
        var candidates = await pracownik.GetAsync($"/api/companies/{demo}/ppk/candidates");
        var declaration = await pracownik.PostAsJsonAsync(
            $"/api/employees/{own}/ppk/declarations",
            new { kind = "resignation", submittedAt = "2026-11-02" }
        );

        Assert.Equal("active", mine.GetProperty("status").GetString());
        Assert.Equal("2026-04-10", mine.GetProperty("enrolledAt").GetString());
        Assert.Equal(2m, mine.GetProperty("employeeBasicRate").GetDecimal());
        Assert.Equal(1.5m, mine.GetProperty("employerBasicRate").GetDecimal());
        Assert.Single(mine.GetProperty("ratePeriods").EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, foreignPpk.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, settings.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, candidates.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, declaration.StatusCode);
    }

    private async Task<Guid> EmployeeAsync(Guid company)
    {
        var employee = await TestDb.AdminScalarAsync<Guid>(
            database,
            """
            INSERT INTO employees.employees (company_id, number, first_name, last_name, birth_date)
            VALUES ($1, left(gen_random_uuid()::text, 20), 'Jan', 'Kowalski', DATE '1990-01-01') RETURNING id
            """,
            new NpgsqlParameter<Guid> { TypedValue = company }
        );
        var employment = await TestDb.AdminScalarAsync<Guid>(
            database,
            """
            INSERT INTO employment.employments (employee_id, company_id, number, kind, contract_type, signed_at,
                                                start_date)
            VALUES ($1, $2, gen_random_uuid()::text, 'employment', 'indefinite', DATE '2026-01-01', DATE '2026-01-01')
            RETURNING id
            """,
            new NpgsqlParameter<Guid> { TypedValue = employee },
            new NpgsqlParameter<Guid> { TypedValue = company }
        );
        await TestDb.AdminScalarAsync<object>(
            database,
            """
            INSERT INTO employment.terms (employment_id, valid_from, fte_numerator, fte_denominator, pay_basis,
                                          base_amount, insurances, cost_kind)
            VALUES ($1, DATE '2026-01-01', 1, 1, 'monthly', 6000,
                    ARRAY['pension', 'disability', 'sickness', 'health'], 'standard')
            """,
            new NpgsqlParameter<Guid> { TypedValue = employment }
        );
        return employee;
    }
}
