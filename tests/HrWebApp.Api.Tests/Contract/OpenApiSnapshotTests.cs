using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using HrWebApp.Api.Tests.Infrastructure;

namespace HrWebApp.Api.Tests.Contract;

/// <summary>
/// Migawka OpenAPI (apps/web/openapi/hr-v1.json) musi zgadzać się z dokumentem API — z niej orval generuje klienta
/// frontu. Aktualizacja: UPDATE_OPENAPI_SNAPSHOT=1 dotnet test --filter OpenApiSnapshotTests, potem yarn api:gen.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class OpenApiSnapshotTests(DatabaseFixture database)
{
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [Fact]
    public async Task Snapshot_matches_api_document()
    {
        using var client = database.Api.CreateClient();
        var document = JsonNode.Parse(await client.GetStringAsync("/openapi/v1.json"))!;
        var actual = document.ToJsonString(Indented).ReplaceLineEndings("\n") + "\n";
        var path = RepoPaths.Combine("apps", "web", "openapi", "hr-v1.json");

        if (Environment.GetEnvironmentVariable("UPDATE_OPENAPI_SNAPSHOT") == "1")
        {
            await File.WriteAllTextAsync(path, actual);
        }

        Assert.True(
            File.Exists(path),
            $"Brak migawki {path}. Uruchom test z UPDATE_OPENAPI_SNAPSHOT=1."
        );
        Assert.Equal(await File.ReadAllTextAsync(path), actual);
    }
}
