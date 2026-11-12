using System.Net;
using System.Text.Json;
using HrWebApp.Api.Tests.Infrastructure;

namespace HrWebApp.Api.Tests.Features.LegalParameters;

[Collection(DatabaseCollection.Name)]
public sealed class LegalParameterHttpTests(DatabaseFixture database)
{
    [Fact]
    public async Task Administrator_lists_parameters_in_force_on_a_day()
    {
        using var admin = await database.Api.ClientAsAsync("administrator");

        var list = await admin.GetFromJsonAsync<JsonElement>(
            "/api/legal-parameters?code=placa.minimalna&onDate=2026-11-30"
        );

        var codes = list.GetProperty("items")
            .EnumerateArray()
            .Select(i => i.GetProperty("code").GetString());
        Assert.Contains("placa.minimalna", codes);
        Assert.Equal(
            4806.00m,
            list.GetProperty("items")
                .EnumerateArray()
                .First(i => i.GetProperty("code").GetString() == "placa.minimalna")
                .GetProperty("value")
                .GetDecimal()
        );
    }

    [Fact]
    public async Task Kadry_get_403_and_invalid_input_gets_field_codes()
    {
        using var kadry = await database.Api.ClientAsAsync("kadry");
        using var admin = await database.Api.ClientAsAsync("administrator");

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await kadry.GetAsync("/api/legal-parameters")).StatusCode
        );

        var response = await admin.PostAsJsonAsync(
            "/api/legal-parameters",
            new
            {
                code = "Zły Kod",
                value = (decimal?)null,
                validFrom = "2026-01-01",
                source = "",
            }
        );
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty(
            "errors"
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid", errors.GetProperty("code")[0].GetString());
        Assert.Equal("required", errors.GetProperty("value")[0].GetString());
        Assert.Equal("required", errors.GetProperty("source")[0].GetString());
    }
}
