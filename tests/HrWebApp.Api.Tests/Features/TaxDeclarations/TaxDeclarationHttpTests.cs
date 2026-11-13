using System.Net;
using System.Text.Json;
using HrWebApp.Api.Tests.Infrastructure;
using Npgsql;

namespace HrWebApp.Api.Tests.Features.TaxDeclarations;

[Collection(DatabaseCollection.Name)]
public sealed class TaxDeclarationHttpTests(DatabaseFixture database)
{
    [Fact]
    public async Task Kadry_create_list_end_and_delete_a_declaration()
    {
        using var kadry = await database.Api.ClientAsAsync("kadry");
        var employee = await EmployeeAsync();

        var invalid = await kadry.PostAsJsonAsync(
            $"/api/employees/{employee}/tax-declarations",
            new
            {
                kind = "pit2",
                submittedAt = "2024-03-20",
                validFrom = "2024-04-01",
            }
        );
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("share", await invalid.Content.ReadAsStringAsync());

        var created = await kadry.PostAsJsonAsync(
            $"/api/employees/{employee}/tax-declarations",
            new
            {
                kind = "pit2",
                share = "1/12",
                submittedAt = "2024-03-20",
                validFrom = "2024-04-01",
            }
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();

        var item = await SingleAsync(kadry, employee);
        Assert.Equal("1/12", item.GetProperty("share").GetString());
        Assert.Equal("active", item.GetProperty("status").GetString());

        var ended = await kadry.PostAsJsonAsync(
            $"/api/tax-declarations/{id}/end",
            new
            {
                rowVersion = item.GetProperty("rowVersion").GetInt64(),
                withdrawnAt = "2024-06-10",
                stopFrom = "2024-07-01",
            }
        );
        Assert.Equal(HttpStatusCode.NoContent, ended.StatusCode);

        item = await SingleAsync(kadry, employee);
        Assert.Equal("2024-06-30", item.GetProperty("validTo").GetString());
        Assert.Equal("ended", item.GetProperty("status").GetString());
        Assert.Empty(
            await kadry.GetFromJsonAsync<JsonElement[]>(
                $"/api/employees/{employee}/tax-declarations?onlyActive=true"
            ) ?? []
        );

        var stale = await kadry.DeleteAsync($"/api/tax-declarations/{id}?rowVersion=1");
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var deleted = await kadry.DeleteAsync(
            $"/api/tax-declarations/{id}?rowVersion={item.GetProperty("rowVersion").GetInt64()}"
        );
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Fact]
    public async Task Pracownik_reads_own_declarations_and_gets_403_on_write_and_foreign_record()
    {
        using var pracownik = await database.Api.ClientAsAsync("pracownik");
        var own = await TestDb.AdminScalarAsync<Guid>(
            database,
            "SELECT l.employee_id FROM platform.user_employee_links l JOIN platform.users u ON u.id = l.user_id WHERE u.login = 'pracownik' LIMIT 1"
        );

        var list = await pracownik.GetAsync($"/api/employees/{own}/tax-declarations");
        var foreign = await pracownik.GetAsync(
            $"/api/employees/{await EmployeeAsync()}/tax-declarations"
        );
        var write = await pracownik.PostAsJsonAsync(
            $"/api/employees/{own}/tax-declarations",
            new
            {
                kind = "higherRate",
                submittedAt = "2024-03-20",
                validFrom = "2024-04-01",
            }
        );

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    private static async Task<JsonElement> SingleAsync(HttpClient client, Guid employee) =>
        Assert.Single(
            await client.GetFromJsonAsync<JsonElement[]>(
                $"/api/employees/{employee}/tax-declarations"
            ) ?? []
        );

    private async Task<Guid> EmployeeAsync()
    {
        var company = await TestDb.CompanyAsync(database);
        return await TestDb.AdminScalarAsync<Guid>(
            database,
            """
            INSERT INTO employees.employees (company_id, number, first_name, last_name, birth_date)
            VALUES ($1, '1', 'Anna', 'Testowa', DATE '1988-04-12') RETURNING id
            """,
            new NpgsqlParameter<Guid> { TypedValue = company }
        );
    }
}
