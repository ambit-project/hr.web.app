using HrWebApp.Api.Tests.Infrastructure;
using Npgsql;

namespace HrWebApp.Api.Tests.Database;

[Collection(DatabaseCollection.Name)]
public sealed class PositionRoutineTests(DatabaseFixture database)
{
    [Fact]
    public async Task Create_returns_id_trims_name_and_writes_audit()
    {
        var name = Unique.Name("Księgowa");

        var id = await CreateAsync($"  {name}  ");

        Assert.Equal(
            name,
            await TestDb.AdminScalarAsync<string>(
                database,
                "SELECT name FROM dictionaries.positions WHERE id = $1",
                new NpgsqlParameter<Guid> { TypedValue = id }
            )
        );
        Assert.Equal(
            await TestDb.DevUserAsync(database, "kadry"),
            await TestDb.AdminScalarAsync<Guid>(
                database,
                "SELECT actor_id FROM platform.audit_entries WHERE entity = 'position' AND entity_id = $1 AND action = 'create'",
                new NpgsqlParameter<string> { TypedValue = id.ToString() }
            )
        );
    }

    [Theory]
    [InlineData("", "required")]
    [InlineData("   ", "required")]
    public async Task Create_rejects_empty_name(string name, string code)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => CreateAsync(name));

        Assert.Equal("U0001", error.SqlState);
        Assert.Equal($$"""{"name": ["{{code}}"]}""", error.Hint);
    }

    [Fact]
    public async Task Create_rejects_name_longer_than_200()
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            CreateAsync(new string('a', 201))
        );

        Assert.Equal("""{"name": ["maxLength"]}""", error.Hint);
    }

    [Fact]
    public async Task Create_rejects_duplicate_name_ignoring_case()
    {
        var name = Unique.Name("Kierownik");
        await CreateAsync(name);

        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            CreateAsync(name.ToUpperInvariant())
        );

        Assert.Equal("U0001", error.SqlState);
        Assert.Equal("""{"name": ["duplicate"]}""", error.Hint);
    }

    [Theory]
    [InlineData("pracownik")]
    [InlineData(null)]
    public async Task Create_and_list_require_kadry_or_administrator(string? login)
    {
        Guid? actor = login is null ? Guid.Empty : await TestDb.DevUserAsync(database, login);

        var create = await Assert.ThrowsAsync<PostgresException>(() =>
            CreateAsync(Unique.Name("Stanowisko"), actor)
        );
        var list = await Assert.ThrowsAsync<PostgresException>(() => ListNamesAsync("x", actor));

        Assert.Equal("U0004", create.SqlState);
        Assert.Equal("U0004", list.SqlState);
    }

    [Fact]
    public async Task List_treats_like_wildcards_literally()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        await CreateAsync($"Stanowisko {marker} 100%");
        await CreateAsync($"Stanowisko {marker} 1000");

        var names = await ListNamesAsync($"{marker} 100%");

        Assert.Equal([$"Stanowisko {marker} 100%"], names);
    }

    [Fact]
    public async Task Api_role_has_no_rights_to_tables()
    {
        await using var connection = await TestDb.ApiAsync(database);
        await using var command = TestDb.Command(
            connection,
            "SELECT count(*) FROM dictionaries.positions"
        );

        var error = await Assert.ThrowsAsync<PostgresException>(command.ExecuteScalarAsync);

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }

    private async Task<Guid> CreateAsync(string name, Guid? actor = null)
    {
        await using var connection = await TestDb.ApiAsync(
            database,
            actor ?? await TestDb.DevUserAsync(database, "kadry")
        );
        await using var command = TestDb.Command(
            connection,
            "CALL dictionaries_app.position_create($1, NULL, NULL)",
            new NpgsqlParameter<string> { TypedValue = name }
        );
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    private async Task<List<string>> ListNamesAsync(string search, Guid? actor = null)
    {
        await using var connection = await TestDb.ApiAsync(
            database,
            actor ?? await TestDb.DevUserAsync(database, "kadry")
        );
        await using var command = TestDb.Command(
            connection,
            "SELECT name FROM dictionaries_app.position_list($1, true, 100, 0)",
            new NpgsqlParameter<string> { TypedValue = search }
        );
        await using var reader = await command.ExecuteReaderAsync();
        var names = new List<string>();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
