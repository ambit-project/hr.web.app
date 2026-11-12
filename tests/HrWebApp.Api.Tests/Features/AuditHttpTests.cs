using System.Net;
using System.Text.Json;
using HrWebApp.Api.Tests.Infrastructure;

namespace HrWebApp.Api.Tests.Features;

[Collection(DatabaseCollection.Name)]
public sealed class AuditHttpTests(DatabaseFixture database)
{
    [Fact]
    public async Task Administrator_sees_who_changed_what_and_kadry_get_403()
    {
        using var admin = await database.Api.ClientAsAsync("administrator");
        using var kadry = await database.Api.ClientAsAsync("kadry");
        var name = Unique.Name("Audytowane stanowisko");
        var created = await kadry.PostAsJsonAsync("/api/positions", new { name });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();

        var audit = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/audit?entity=position&entityId={id}"
        );
        var entry = audit.GetProperty("items")[0];

        Assert.Equal("create", entry.GetProperty("action").GetString());
        Assert.Equal("Kadry (dev)", entry.GetProperty("actorName").GetString());
        Assert.Equal(
            name,
            JsonElement
                .Parse(entry.GetProperty("after").GetString()!)
                .GetProperty("name")
                .GetString()
        );
        Assert.Equal(HttpStatusCode.Forbidden, (await kadry.GetAsync("/api/audit")).StatusCode);
    }
}
