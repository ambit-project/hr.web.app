using System.Net;
using System.Text.Json;
using HrWebApp.Api.Tests.Infrastructure;
using Npgsql;

namespace HrWebApp.Api.Tests.Features.PayComponents;

[Collection(DatabaseCollection.Name)]
public sealed class PayComponentHttpTests(DatabaseFixture database)
{
    [Fact]
    public async Task Admin_defines_a_deduction_and_kadry_assign_it_with_a_signed_consent()
    {
        using var admin = await database.Api.ClientAsAsync("administrator");
        using var kadry = await database.Api.ClientAsAsync("kadry");
        var (employee, employment) = await ContractAsync();

        var forbidden = await kadry.PostAsJsonAsync(
            "/api/pay-component-types",
            new
            {
                code = Unique.Code(),
                name = "x",
                kind = "addition",
                calculation = "fixed",
            }
        );
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var created = await admin.PostAsJsonAsync(
            "/api/pay-component-types",
            new
            {
                code = Unique.Code(),
                name = "Karta sportowa",
                kind = "deduction",
                calculation = "fixed",
                deductionCategory = "voluntaryOther",
            }
        );
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var type = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();

        var withoutConsent = await kadry.PostAsJsonAsync(
            $"/api/employments/{employment}/components",
            new
            {
                componentTypeId = type,
                value = 100m,
                validFrom = "2025-01-01",
            }
        );
        Assert.Equal(HttpStatusCode.BadRequest, withoutConsent.StatusCode);

        var consent = (
            await (
                await kadry.PostAsJsonAsync(
                    $"/api/employees/{employee}/deduction-consents",
                    new
                    {
                        componentTypeId = type,
                        monthlyAmount = 100m,
                        validFrom = "2025-01-01",
                        signedAt = "2024-12-20",
                    }
                )
            ).Content.ReadFromJsonAsync<JsonElement>()
        ).GetProperty("id").GetGuid();
        var added = await kadry.PostAsJsonAsync(
            $"/api/employments/{employment}/components",
            new
            {
                componentTypeId = type,
                value = 100m,
                validFrom = "2025-01-01",
                consentId = consent,
            }
        );
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        var components = await kadry.GetFromJsonAsync<JsonElement[]>(
            $"/api/employments/{employment}/components"
        );
        Assert.Equal(100m, Assert.Single(components!).GetProperty("value").GetDecimal());

        var consents = await kadry.GetFromJsonAsync<JsonElement[]>(
            $"/api/employees/{employee}/deduction-consents"
        );
        var item = Assert.Single(consents!);
        var deleteInUse = await kadry.DeleteAsync(
            $"/api/deduction-consents/{consent}?rowVersion={item.GetProperty("rowVersion").GetInt64()}"
        );
        Assert.Equal(HttpStatusCode.Conflict, deleteInUse.StatusCode);
        Assert.Contains("consentInUse", await deleteInUse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Kadry_register_a_garnishment_and_pracownik_cannot_see_it()
    {
        using var kadry = await database.Api.ClientAsAsync("kadry");
        using var pracownik = await database.Api.ClientAsAsync("pracownik");
        var (employee, _) = await ContractAsync();

        var created = await kadry.PostAsJsonAsync(
            $"/api/employees/{employee}/garnishments",
            new
            {
                kind = "other",
                authorityKind = "court",
                authorityName = "Komornik Sądowy przy SR Testowo",
                caseNumber = "Km 5/25",
                receivedAt = "2025-02-10",
                totalAmount = 6000m,
            }
        );
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        var list = await kadry.GetFromJsonAsync<JsonElement[]>(
            $"/api/employees/{employee}/garnishments"
        );
        var item = Assert.Single(list!);
        Assert.Equal("active", item.GetProperty("status").GetString());

        var status = await kadry.PostAsJsonAsync(
            $"/api/garnishments/{item.GetProperty("id").GetGuid()}/status",
            new
            {
                rowVersion = item.GetProperty("rowVersion").GetInt64(),
                status = "paidOff",
                statusDate = "2025-06-30",
            }
        );
        Assert.Equal(HttpStatusCode.NoContent, status.StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await pracownik.GetAsync($"/api/employees/{employee}/garnishments")).StatusCode
        );
    }

    private async Task<(Guid Employee, Guid Employment)> ContractAsync()
    {
        var company = await TestDb.CompanyAsync(database);
        var employee = await TestDb.AdminScalarAsync<Guid>(
            database,
            """
            INSERT INTO employees.employees (company_id, number, first_name, last_name, birth_date)
            VALUES ($1, '1', 'Jan', 'Kowalski', DATE '1985-01-01') RETURNING id
            """,
            new NpgsqlParameter<Guid> { TypedValue = company }
        );
        var employment = await TestDb.AdminScalarAsync<Guid>(
            database,
            """
            INSERT INTO employment.employments (employee_id, company_id, number, kind, contract_type, signed_at,
                                                start_date)
            VALUES ($1, $2, gen_random_uuid()::text, 'employment', 'indefinite', DATE '2024-01-01', DATE '2024-01-01')
            RETURNING id
            """,
            new NpgsqlParameter<Guid> { TypedValue = employee },
            new NpgsqlParameter<Guid> { TypedValue = company }
        );
        return (employee, employment);
    }
}
