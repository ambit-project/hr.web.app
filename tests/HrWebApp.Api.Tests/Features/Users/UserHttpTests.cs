using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using HrWebApp.Api.Tests.Infrastructure;

namespace HrWebApp.Api.Tests.Features.Users;

[Collection(DatabaseCollection.Name)]
public sealed class UserHttpTests(DatabaseFixture database)
{
    private const string Password = "haslo-poczatkowe-1";
    private static readonly string[] KadryOnly = ["kadry"];

    [Fact]
    public async Task Administrator_creates_account_that_can_log_in()
    {
        using var admin = await database.Api.ClientAsAsync("administrator");
        var login = Unique.Login();

        var created = await admin.PostAsJsonAsync(
            "/api/users",
            new
            {
                login,
                email = $"{login}@test.example",
                displayName = "Nowa Kadrowa",
                roles = KadryOnly,
                password = Password,
            }
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var client = database.Api.CreateClient();
        var session = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { login, password = Password }
        );
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
    }

    [Fact]
    public async Task Short_password_and_duplicates_are_field_errors()
    {
        using var admin = await database.Api.ClientAsAsync("administrator");

        var response = await admin.PostAsJsonAsync(
            "/api/users",
            new
            {
                login = "kadry",
                email = "nie-email",
                displayName = "X",
                roles = Array.Empty<string>(),
                password = "krotkie",
            }
        );
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty(
            "errors"
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("duplicate", errors.GetProperty("login")[0].GetString());
        Assert.Equal("invalid", errors.GetProperty("email")[0].GetString());
        Assert.Equal("required", errors.GetProperty("roles")[0].GetString());
        Assert.Equal("tooShort", errors.GetProperty("password")[0].GetString());
    }

    [Fact]
    public async Task Deactivation_ends_sessions_and_administrator_cannot_lock_self_out()
    {
        var login = Unique.Login();
        var adminLogin = Unique.Login();
        var id = await TestDb.CreateUserAsync(database, login, Password, "kadry");
        await TestDb.CreateUserAsync(database, adminLogin, Password, "administrator");
        using var user = await LoginAsync(login);
        using var admin = await LoginAsync(adminLogin);

        var item = await FindAsync(admin, login);
        var deactivate = await admin.PutAsJsonAsync(
            $"/api/users/{id}",
            new
            {
                rowVersion = item.GetProperty("rowVersion").GetInt64(),
                email = $"{login}@test.example",
                displayName = login,
                roles = KadryOnly,
                isActive = false,
            }
        );
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await user.GetAsync("/api/auth/me")).StatusCode);

        var self = await FindAsync(admin, adminLogin);
        var lockout = await admin.PutAsJsonAsync(
            $"/api/users/{self.GetProperty("id").GetGuid()}",
            new
            {
                rowVersion = self.GetProperty("rowVersion").GetInt64(),
                email = $"{adminLogin}@test.example",
                displayName = adminLogin,
                roles = KadryOnly,
                isActive = true,
            }
        );
        Assert.Equal(HttpStatusCode.Conflict, lockout.StatusCode);
    }

    [Fact]
    public async Task New_password_unlocks_account_and_kadry_cannot_manage_users()
    {
        var login = Unique.Login();
        var id = await TestDb.CreateUserAsync(database, login, Password, "kadry");
        using var admin = await database.Api.ClientAsAsync("administrator");
        using var kadry = await database.Api.ClientAsAsync("kadry");

        Assert.Equal(HttpStatusCode.Forbidden, (await kadry.GetAsync("/api/users")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (
                await admin.PutAsJsonAsync(
                    $"/api/users/{id}/password",
                    new { password = "nowe-haslo-123" }
                )
            ).StatusCode
        );

        using var client = database.Api.CreateClient();
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await client.PostAsJsonAsync(
                    "/api/auth/login",
                    new { login, password = "nowe-haslo-123" }
                )
            ).StatusCode
        );
    }

    private async Task<HttpClient> LoginAsync(string login)
    {
        var client = database.Api.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { login, password = Password }
        );
        response.EnsureSuccessStatusCode();
        var token = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("token")
            .GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> FindAsync(HttpClient client, string login)
    {
        var list = await client.GetFromJsonAsync<JsonElement>($"/api/users?search={login}");
        return list.GetProperty("items")[0];
    }
}
