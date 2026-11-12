using System.Net;
using System.Text.Json;
using HrWebApp.Api.Tests.Infrastructure;

namespace HrWebApp.Api.Tests.Features.Companies;

[Collection(DatabaseCollection.Name)]
public sealed class CompanyHttpTests(DatabaseFixture database)
{
    [Fact]
    public async Task Administrator_creates_and_edits_kadry_only_reads()
    {
        using var admin = await database.Api.ClientAsAsync("administrator");
        using var kadry = await database.Api.ClientAsAsync("kadry");
        var shortName = Unique.Login();
        var company = NewCompany(shortName);

        var created = await admin.PostAsJsonAsync("/api/companies", company);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await kadry.PostAsJsonAsync("/api/companies", NewCompany(Unique.Login()))).StatusCode
        );

        var item = await FindAsync(kadry, shortName);
        var update = new
        {
            rowVersion = item.GetProperty("rowVersion").GetInt64(),
            shortName,
            name = "Spółka po zmianie sp. z o.o.",
            nip = item.GetProperty("nip").GetString(),
            regon = item.GetProperty("regon").GetString(),
            street = "ul. Nowa 2",
            postalCode = "00-002",
            city = "Przykładowo",
            isActive = true,
        };
        var id = item.GetProperty("id").GetGuid();

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await admin.PutAsJsonAsync($"/api/companies/{id}", update)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await admin.PutAsJsonAsync($"/api/companies/{id}", update)).StatusCode
        );
        Assert.Equal(
            "Spółka po zmianie sp. z o.o.",
            (await FindAsync(kadry, shortName)).GetProperty("name").GetString()
        );
    }

    private static object NewCompany(string shortName) =>
        new
        {
            shortName,
            name = "Spółka Testowa sp. z o.o.",
            nip = FakeIds.Nip(),
            regon = FakeIds.Regon(),
            street = "ul. Testowa 1",
            postalCode = "00-001",
            city = "Przykładowo",
        };

    private static async Task<JsonElement> FindAsync(HttpClient client, string shortName)
    {
        var list = await client.GetFromJsonAsync<JsonElement>(
            $"/api/companies?search={Uri.EscapeDataString(shortName)}"
        );
        return list.GetProperty("items")[0];
    }
}
