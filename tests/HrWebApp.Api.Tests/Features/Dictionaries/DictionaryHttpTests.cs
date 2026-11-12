using System.Net;
using System.Text.Json;
using HrWebApp.Api.Tests.Infrastructure;

namespace HrWebApp.Api.Tests.Features.Dictionaries;

[Collection(DatabaseCollection.Name)]
public sealed class DictionaryHttpTests(DatabaseFixture database)
{
    [Fact]
    public async Task Kadry_read_zus_codes_and_tax_offices_but_cannot_write_them()
    {
        using var kadry = await database.Api.ClientAsAsync("kadry");

        var codes = await kadry.GetFromJsonAsync<JsonElement>(
            "/api/zus-codes?kind=title&search=0110&onDate=2026-10-01"
        );
        var offices = await kadry.GetFromJsonAsync<JsonElement>("/api/tax-offices?pageSize=5");

        Assert.Equal("0110", codes.GetProperty("items")[0].GetProperty("code").GetString());
        Assert.Equal(5, offices.GetProperty("items").GetArrayLength());
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (
                await kadry.PostAsJsonAsync(
                    "/api/zus-codes",
                    new
                    {
                        kind = "title",
                        code = "9999",
                        name = "X",
                        validFrom = "2026-01-01",
                    }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (
                await kadry.PostAsJsonAsync("/api/tax-offices", new { code = "9999", name = "X" })
            ).StatusCode
        );
    }

    [Fact]
    public async Task Administrator_validation_errors_come_back_per_field()
    {
        using var admin = await database.Api.ClientAsAsync("administrator");

        var response = await admin.PostAsJsonAsync(
            "/api/zus-codes",
            new
            {
                kind = "pension",
                code = "12",
                name = "",
                validFrom = "2026-02-01",
                validTo = "2026-01-01",
            }
        );
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty(
            "errors"
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid", errors.GetProperty("code")[0].GetString());
        Assert.Equal("required", errors.GetProperty("name")[0].GetString());
        Assert.Equal("beforeFrom", errors.GetProperty("validTo")[0].GetString());
    }

    [Fact]
    public async Task Position_edit_with_stale_row_version_is_409()
    {
        using var kadry = await database.Api.ClientAsAsync("kadry");
        var name = Unique.Name("Edytowane stanowisko");
        await kadry.PostAsJsonAsync("/api/positions", new { name });
        var item = (
            await kadry.GetFromJsonAsync<JsonElement>(
                $"/api/positions?search={Uri.EscapeDataString(name)}"
            )
        ).GetProperty("items")[0];
        var id = item.GetProperty("id").GetGuid();
        var update = new
        {
            rowVersion = item.GetProperty("rowVersion").GetInt64(),
            name,
            description = "Opis",
            isActive = true,
        };

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await kadry.PutAsJsonAsync($"/api/positions/{id}", update)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await kadry.PutAsJsonAsync($"/api/positions/{id}", update)).StatusCode
        );
    }
}
