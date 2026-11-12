using System.Net;
using System.Text.Json;
using HrWebApp.Api.Tests.Infrastructure;

namespace HrWebApp.Api.Tests.Features.Positions;

[Collection(DatabaseCollection.Name)]
public sealed class PositionHttpTests(DatabaseFixture database)
{
    private readonly ApiFactory _factory = database.Api;

    [Fact]
    public async Task Post_creates_position_visible_in_list()
    {
        using var client = await _factory.ClientAsAsync("kadry");
        var name = Unique.Name("Specjalista ds. płac");

        var created = await client.PostAsJsonAsync(
            "/api/positions",
            new { name, description = "Opis" }
        );

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetGuid();
        Assert.Equal($"/api/positions/{id}", created.Headers.Location?.OriginalString);

        var list = await client.GetFromJsonAsync<JsonElement>(
            $"/api/positions?search={Uri.EscapeDataString(name)}"
        );
        Assert.Equal(1, list.GetProperty("total").GetInt64());
        Assert.Equal(id, list.GetProperty("items")[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Post_duplicate_returns_validation_problem_with_codes()
    {
        using var client = await _factory.ClientAsAsync("administrator");
        var name = Unique.Name("Kadrowa");
        await client.PostAsJsonAsync("/api/positions", new { name });

        var response = await client.PostAsJsonAsync("/api/positions", new { name });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("U0001", problem.GetProperty("code").GetString());
        Assert.Equal("duplicate", problem.GetProperty("errors").GetProperty("name")[0].GetString());
    }

    [Fact]
    public async Task Health_returns_ok()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
