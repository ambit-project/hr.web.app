using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HrWebApp.Api.Tests.Infrastructure;

public sealed class ApiFactory(
    DatabaseFixture database,
    string environment = "Development",
    IReadOnlyDictionary<string, string?>? settings = null
) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting("ConnectionStrings:Hr", database.ApiConnectionString);
        builder.UseSetting("Auth:DevLogin", "true");
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }
    }

    /// <summary>Klient z sesją konta deweloperskiego o danej roli (kadry, administrator, pracownik).</summary>
    public async Task<HttpClient> ClientAsAsync(string role)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/dev-login", new { role });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            body.GetProperty("token").GetString()
        );
        return client;
    }
}
