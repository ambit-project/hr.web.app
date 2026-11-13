using System.Net;
using System.Text.Json;
using HrWebApp.Api.Tests.Infrastructure;
using Npgsql;

namespace HrWebApp.Api.Tests.Features.Employees;

[Collection(DatabaseCollection.Name)]
public sealed class EmployeeHttpTests(DatabaseFixture database)
{
    [Fact]
    public async Task Kadry_create_read_edit_and_delete_an_employee()
    {
        using var kadry = await database.Api.ClientAsAsync("kadry");
        var company = await TestDb.CompanyAsync(database);
        var taxOfficeId = await TestDb.TaxOfficeAsync(database);
        var pesel = FakeIds.Pesel(new DateOnly(2002, 9, 15));
        var iban = FakeIds.Iban();

        var created = await kadry.PostAsJsonAsync(
            "/api/employees",
            new
            {
                companyId = company,
                firstName = "Piotr",
                lastName = "Testowy",
                pesel,
                birthDate = "2002-09-15",
                taxOfficeId,
                street = "ul. Lipowa 1",
                postalCode = "00-001",
                city = "Przykładowo",
                addressFrom = "2020-01-01",
            }
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await kadry.PostAsJsonAsync(
                    $"/api/employees/{id}/bank-accounts",
                    new { iban, validFrom = "2026-10-01" }
                )
            ).StatusCode
        );

        var details = await kadry.GetFromJsonAsync<JsonElement>($"/api/employees/{id}");
        Assert.Equal(pesel, details.GetProperty("pesel").GetString());
        Assert.Equal(iban, details.GetProperty("iban").GetString());
        Assert.Equal("1", details.GetProperty("number").GetString());
        // Akronim nadany przy założeniu: RRRRMM dnia dodania + 4 znaki bez mylących (0/O, 1/I/L).
        Assert.Matches(
            $"^{DateTime.Today:yyyyMM}[A-HJKMNP-Z2-9]{{4}}$",
            details.GetProperty("acronym").GetString()
        );

        var update = new
        {
            rowVersion = details.GetProperty("rowVersion").GetInt64(),
            number = "P/1",
            acronym = "KOWALSKI-P",
            firstName = "Piotr",
            lastName = "Testowy-Nowy",
            pesel,
            birthDate = "2002-09-15",
            taxOfficeId,
        };
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await kadry.PutAsJsonAsync($"/api/employees/{id}", update)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await kadry.PutAsJsonAsync($"/api/employees/{id}", update)).StatusCode
        );

        var list = await kadry.GetFromJsonAsync<JsonElement>(
            $"/api/employees?companyId={company}&search=nowy"
        );
        Assert.Equal("P/1", list.GetProperty("items")[0].GetProperty("number").GetString());
        var byAcronym = await kadry.GetFromJsonAsync<JsonElement>(
            $"/api/employees?companyId={company}&search=kowalski-p"
        );
        Assert.Equal(
            "KOWALSKI-P",
            byAcronym.GetProperty("items")[0].GetProperty("acronym").GetString()
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await kadry.DeleteAsync($"/api/employees/{id}")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await kadry.GetAsync($"/api/employees/{id}")).StatusCode
        );
    }

    [Fact]
    public async Task Linked_account_sees_profiles_and_pracownik_gets_403_on_employees()
    {
        using var kadry = await database.Api.ClientAsAsync("kadry");
        using var pracownik = await database.Api.ClientAsAsync("pracownik");
        var company = await TestDb.CompanyAsync(database);
        var employee = await TestDb.AdminScalarAsync<Guid>(
            database,
            """
            INSERT INTO employees.employees (company_id, number, first_name, last_name, birth_date)
            VALUES ($1, '1', 'Anna', 'Testowa', DATE '1988-04-12') RETURNING id
            """,
            new NpgsqlParameter<Guid> { TypedValue = company }
        );

        var linkToKadry = await kadry.PutAsJsonAsync(
            $"/api/employees/{employee}/account",
            new { account = "kadry" }
        );
        var link = await kadry.PutAsJsonAsync(
            $"/api/employees/{employee}/account",
            new { account = "pracownik@dev.example" }
        );
        var me = await pracownik.GetFromJsonAsync<JsonElement>("/api/auth/me");

        Assert.Equal(HttpStatusCode.BadRequest, linkToKadry.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, link.StatusCode);
        Assert.Contains(me.GetProperty("profiles").EnumerateArray(), profile =>
            profile.GetProperty("employeeId").GetGuid() == employee
        );
        Assert.Contains(me.GetProperty("profiles").EnumerateArray(), profile =>
            profile.GetProperty("companyShortName").GetString() == "Demo"
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await pracownik.GetAsync("/api/employees")).StatusCode
        );
    }

    [Fact]
    public async Task Api_does_not_start_without_encryption_key()
    {
        await using var factory = new ApiFactory(
            database,
            settings: new Dictionary<string, string?> { ["Data:EncryptionKey"] = "za-krótki" }
        );

        var error = Assert.ThrowsAny<Exception>(factory.CreateClient);

        Assert.Contains("EncryptionKey", error.ToString());
    }
}
