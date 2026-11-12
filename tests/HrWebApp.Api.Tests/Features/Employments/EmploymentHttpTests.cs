using System.Net;
using System.Text.Json;
using HrWebApp.Api.Tests.Infrastructure;
using Npgsql;

namespace HrWebApp.Api.Tests.Features.Employments;

[Collection(DatabaseCollection.Name)]
public sealed class EmploymentHttpTests(DatabaseFixture database)
{
    private static readonly string[] HealthOnly = ["health"];

    [Fact]
    public async Task Kadry_create_read_change_terms_terminate_and_delete_a_contract()
    {
        using var kadry = await database.Api.ClientAsAsync("kadry");
        var employee = await EmployeeAsync();
        var position = await TestDb.AdminScalarAsync<Guid>(
            database,
            "INSERT INTO dictionaries.positions (name) VALUES ($1) RETURNING id",
            new NpgsqlParameter<string> { TypedValue = Unique.Name("Stanowisko") }
        );
        string[] insurances = ["pension", "disability", "sickness", "accident", "health"];

        var created = await kadry.PostAsJsonAsync(
            $"/api/employees/{employee}/employments",
            new
            {
                kind = "employment",
                contractType = "indefinite",
                signedAt = "2020-01-01",
                startDate = "2020-01-01",
                positionId = position,
                fteNumerator = 1,
                fteDenominator = 1,
                payBasis = "monthly",
                baseAmount = 6000.00m,
                zusTitleCode = "0110",
                zusPensionCode = "0",
                zusDisabilityCode = "0",
                insurances,
                costKind = "standard",
            }
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();

        var raise = await kadry.PostAsJsonAsync(
            $"/api/employments/{id}/terms",
            new
            {
                validFrom = "2020-07-01",
                positionId = position,
                fteNumerator = 1,
                fteDenominator = 1,
                payBasis = "monthly",
                baseAmount = 6500.50m,
                zusTitleCode = "0110",
                zusPensionCode = "0",
                zusDisabilityCode = "0",
                insurances,
                costKind = "standard",
            }
        );
        Assert.Equal(HttpStatusCode.OK, raise.StatusCode);

        var details = await kadry.GetFromJsonAsync<JsonElement>($"/api/employments/{id}");
        Assert.Equal("1", details.GetProperty("number").GetString());
        Assert.Equal(2, details.GetProperty("termsHistory").GetArrayLength());
        Assert.Equal(
            6500.50m,
            details.GetProperty("termsHistory")[0].GetProperty("baseAmount").GetDecimal()
        );

        var terminate = await kadry.PostAsJsonAsync(
            $"/api/employments/{id}/terminate",
            new
            {
                rowVersion = details.GetProperty("rowVersion").GetInt64(),
                terminatedAt = "2020-12-31",
            }
        );
        Assert.Equal(HttpStatusCode.NoContent, terminate.StatusCode);

        var list = await kadry.GetFromJsonAsync<JsonElement>(
            $"/api/employees/{employee}/employments"
        );
        Assert.Equal("ended", list[0].GetProperty("status").GetString());
        Assert.Equal("2030-12-31", list[0].GetProperty("retentionUntil").GetString());

        var deleteEmployee = await kadry.DeleteAsync($"/api/employees/{employee}");
        Assert.Equal(HttpStatusCode.Conflict, deleteEmployee.StatusCode);
        Assert.Equal(
            "retention",
            (await deleteEmployee.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("reason")
                .GetString()
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await kadry.DeleteAsync($"/api/employments/{id}")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await kadry.GetAsync($"/api/employments/{id}")).StatusCode
        );
    }

    [Fact]
    public async Task Mandate_with_own_employee_gets_409_with_reason_and_pracownik_gets_403()
    {
        using var kadry = await database.Api.ClientAsAsync("kadry");
        using var pracownik = await database.Api.ClientAsAsync("pracownik");
        var employee = await EmployeeAsync();
        await TestDb.AdminScalarAsync<object>(
            database,
            """
            INSERT INTO employment.employments (employee_id, company_id, number, kind, contract_type, signed_at, start_date)
            SELECT id, company_id, 'UP/1', 'employment', 'indefinite', DATE '2020-01-01', DATE '2020-01-01'
            FROM employees.employees WHERE id = $1
            """,
            new NpgsqlParameter<Guid> { TypedValue = employee }
        );

        var mandate = await kadry.PostAsJsonAsync(
            $"/api/employees/{employee}/employments",
            new
            {
                kind = "mandate",
                signedAt = "2020-05-01",
                startDate = "2020-05-01",
                endDate = "2020-05-31",
                payBasis = "hourly",
                baseAmount = 40m,
                zusTitleCode = "0411",
                zusPensionCode = "0",
                zusDisabilityCode = "0",
                insurances = HealthOnly,
                costKind = "flat20",
            }
        );

        Assert.Equal(HttpStatusCode.Conflict, mandate.StatusCode);
        Assert.Equal(
            "concurrentTitleUnsupported",
            (await mandate.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("reason")
                .GetString()
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await pracownik.GetAsync($"/api/employees/{employee}/employments")).StatusCode
        );
    }

    private async Task<Guid> EmployeeAsync()
    {
        var company = await TestDb.CompanyAsync(database);
        return await TestDb.AdminScalarAsync<Guid>(
            database,
            """
            INSERT INTO employees.employees (company_id, number, first_name, last_name, birth_date)
            VALUES ($1, '1', 'Piotr', 'Testowy', DATE '1990-01-01') RETURNING id
            """,
            new NpgsqlParameter<Guid> { TypedValue = company }
        );
    }
}
