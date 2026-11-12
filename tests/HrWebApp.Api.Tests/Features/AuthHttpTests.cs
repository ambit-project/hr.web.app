using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using HrWebApp.Api.Tests.Infrastructure;

namespace HrWebApp.Api.Tests.Features;

[Collection(DatabaseCollection.Name)]
public sealed class AuthHttpTests(DatabaseFixture database)
{
    [Fact]
    public async Task Api_requires_session()
    {
        using var client = database.Api.CreateClient();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/positions")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/auth/me")).StatusCode
        );
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            "nieznany"
        );
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/positions")).StatusCode
        );
    }

    [Fact]
    public async Task Login_me_logout()
    {
        var login = Unique.Login();
        await TestDb.CreateUserAsync(database, login, "haslo-do-testu-1", "kadry", "administrator");
        using var client = database.Api.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { login, password = "haslo-do-testu-1" }
        );
        response.EnsureSuccessStatusCode();
        var token = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("token")
            .GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal(login, me.GetProperty("login").GetString());
        Assert.Equal(
            ["administrator", "kadry"],
            me.GetProperty("roles").EnumerateArray().Select(r => r.GetString())
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.PostAsync("/api/auth/logout", null)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/auth/me")).StatusCode
        );
    }

    [Fact]
    public async Task Failed_login_returns_401_with_code()
    {
        using var client = database.Api.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { login = "nie-ma-takiego", password = "x" }
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(
            "U0005",
            (await response.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("code")
                .GetString()
        );
    }

    [Fact]
    public async Task Pracownik_gets_403_on_positions()
    {
        using var client = await database.Api.ClientAsAsync("pracownik");

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/positions")).StatusCode
        );
    }

    [Fact]
    public async Task Dev_login_is_enabled_in_development()
    {
        using var client = database.Api.CreateClient();

        var options = await client.GetFromJsonAsync<JsonElement>("/api/auth/options");

        Assert.True(options.GetProperty("devLogin").GetBoolean());
    }

    [Fact]
    public async Task Dev_login_does_not_exist_outside_development()
    {
        await using var production = new ApiFactory(database, "Production");
        using var client = production.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/dev-login",
            new { role = "administrator" }
        );
        var options = await client.GetFromJsonAsync<JsonElement>("/api/auth/options");

        Assert.True(
            response.StatusCode == HttpStatusCode.NotFound,
            $"status {response.StatusCode}: {await response.Content.ReadAsStringAsync()}"
        );
        Assert.False(options.GetProperty("devLogin").GetBoolean());
    }

    [Fact]
    public async Task Bootstrap_admin_is_created_once()
    {
        var first = Unique.Login();
        var second = Unique.Login();

        await using (var factory = Bootstrap(first))
        {
            factory.CreateClient().Dispose();
        }

        await using (var factory = Bootstrap(second))
        {
            factory.CreateClient().Dispose();
        }

        Assert.True(
            await TestDb.AdminScalarAsync<bool>(
                database,
                "SELECT EXISTS (SELECT FROM platform.users u JOIN platform.user_roles r ON r.user_id = u.id WHERE r.role = 'administrator' AND NOT u.is_dev_account)"
            )
        );
        Assert.Equal(
            0L,
            await TestDb.AdminScalarAsync<long>(
                database,
                "SELECT count(*) FROM platform.users WHERE login = $1",
                new Npgsql.NpgsqlParameter<string> { TypedValue = second }
            )
        );
    }

    private ApiFactory Bootstrap(string login) =>
        new(
            database,
            settings: new Dictionary<string, string?>
            {
                ["Auth:BootstrapAdmin:Login"] = login,
                ["Auth:BootstrapAdmin:Email"] = login + "@test.example",
                ["Auth:BootstrapAdmin:Password"] = "haslo-administratora-1",
            }
        );
}
