using HrWebApp.Api.Tests.Infrastructure;
using Npgsql;

namespace HrWebApp.Api.Tests.Database;

[Collection(DatabaseCollection.Name)]
public sealed class SessionRoutineTests(DatabaseFixture database)
{
    private const string Password = "poprawne-haslo-123";

    [Fact]
    public async Task Login_with_login_or_email_ignoring_case_creates_session()
    {
        var login = Unique.Login();
        var id = await TestDb.CreateUserAsync(database, login, Password, "kadry");

        var byLogin = await LoginAsync($"  {login.ToUpperInvariant()} ", Password, "t1-" + login);
        var byEmail = await LoginAsync($"{login}@test.example", Password, "t2-" + login);

        Assert.Equal(id, byLogin);
        Assert.Equal(id, byEmail);
        var resolved = await ResolveAsync("t1-" + login);
        Assert.Equal(id, resolved?.Item1);
        Assert.Equal(["kadry"], resolved?.Item2 ?? []);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_login_return_null()
    {
        var login = Unique.Login();
        await TestDb.CreateUserAsync(database, login, Password, "kadry");

        Assert.Null(await LoginAsync(login, "zle-haslo", "t-" + login));
        Assert.Null(await LoginAsync("nie-ma-" + login, Password, "t2-" + login));
    }

    [Fact]
    public async Task Account_locks_after_max_failed_attempts_and_unlocks_after_lockout()
    {
        var login = Unique.Login();
        var id = await TestDb.CreateUserAsync(database, login, Password, "kadry");

        for (var i = 0; i < 3; i++)
        {
            await LoginAsync(login, "zle-haslo", $"bad{i}-{login}", maxFailed: 3);
        }

        Assert.Null(await LoginAsync(login, Password, "locked-" + login, maxFailed: 3));

        await TestDb.AdminScalarAsync<object>(
            database,
            "UPDATE platform.users SET locked_until = now() - interval '1 minute' WHERE id = $1",
            new NpgsqlParameter<Guid> { TypedValue = id }
        );
        Assert.Equal(id, await LoginAsync(login, Password, "after-" + login, maxFailed: 3));
    }

    [Fact]
    public async Task Inactive_account_cannot_log_in_and_loses_sessions()
    {
        var login = Unique.Login();
        var id = await TestDb.CreateUserAsync(database, login, Password, "kadry");
        await LoginAsync(login, Password, "s-" + login);

        await TestDb.AdminScalarAsync<object>(
            database,
            "UPDATE platform.users SET is_active = false WHERE id = $1",
            new NpgsqlParameter<Guid> { TypedValue = id }
        );

        Assert.Null(await LoginAsync(login, Password, "s2-" + login));
        Assert.Null(await ResolveAsync("s-" + login));
    }

    [Fact]
    public async Task Expired_and_logged_out_sessions_do_not_resolve()
    {
        var login = Unique.Login();
        await TestDb.CreateUserAsync(database, login, Password, "kadry");
        await LoginAsync(login, Password, "exp-" + login);
        await LoginAsync(login, Password, "out-" + login);

        await TestDb.AdminScalarAsync<object>(
            database,
            "UPDATE platform.sessions SET expires_at = now() - interval '1 second' WHERE token_hash = ext.digest($1, 'sha256')",
            new NpgsqlParameter<string> { TypedValue = "exp-" + login }
        );
        await using (var connection = await TestDb.ApiAsync(database))
        await using (
            var logout = TestDb.Command(
                connection,
                "CALL platform_app.session_logout($1)",
                new NpgsqlParameter<string> { TypedValue = "out-" + login }
            )
        )
        {
            await logout.ExecuteNonQueryAsync();
        }

        Assert.Null(await ResolveAsync("exp-" + login));
        Assert.Null(await ResolveAsync("out-" + login));
    }

    [Fact]
    public async Task Dev_login_uses_only_dev_accounts()
    {
        var kadry = await TestDb.DevUserAsync(database, "kadry");
        await TestDb.CreateUserAsync(database, Unique.Login(), Password, "administrator");

        await using var connection = await TestDb.ApiAsync(database);
        await using var command = TestDb.Command(
            connection,
            "CALL platform_app.session_dev_login($1, $2, 1, NULL)",
            new NpgsqlParameter<string> { TypedValue = "kadry" },
            new NpgsqlParameter<string> { TypedValue = "dev-" + Guid.NewGuid() }
        );

        Assert.Equal(kadry, await command.ExecuteScalarAsync());
        Assert.True(
            await TestDb.AdminScalarAsync<bool>(
                database,
                "SELECT bool_and(is_dev_account) FROM platform.users u JOIN platform.user_roles r ON r.user_id = u.id WHERE u.login IN ('admin', 'kadry', 'pracownik')"
            )
        );
    }

    private async Task<Guid?> LoginAsync(
        string login,
        string password,
        string token,
        int maxFailed = 5
    )
    {
        await using var connection = await TestDb.ApiAsync(database);
        await using var command = TestDb.Command(
            connection,
            "CALL platform_app.session_login($1, $2, $3, 1, $4, 15, NULL)",
            new NpgsqlParameter<string> { TypedValue = login },
            new NpgsqlParameter<string> { TypedValue = password },
            new NpgsqlParameter<string> { TypedValue = token },
            new NpgsqlParameter<int> { TypedValue = maxFailed }
        );
        return await command.ExecuteScalarAsync() as Guid?;
    }

    private async Task<(Guid, string[])?> ResolveAsync(string token)
    {
        await using var connection = await TestDb.ApiAsync(database);
        await using var command = TestDb.Command(
            connection,
            "SELECT user_id, roles FROM platform_app.session_resolve($1)",
            new NpgsqlParameter<string> { TypedValue = token }
        );
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync()
            ? (reader.GetGuid(0), reader.GetFieldValue<string[]>(1))
            : null;
    }
}
