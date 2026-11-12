using HrWebApp.Api.Tests.Infrastructure;
using Npgsql;

namespace HrWebApp.Api.Tests.Database;

[Collection(DatabaseCollection.Name)]
public sealed class PositionRoutineTests(DatabaseFixture database)
{
    [Fact]
    public async Task Create_returns_id_and_trims_name()
    {
        var name = Unique.Name("Księgowa");

        var id = await CreateAsync($"  {name}  ");

        Assert.NotEqual(Guid.Empty, id);
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = Command(
            connection,
            "SELECT name FROM dictionaries.positions WHERE id = $1",
            new NpgsqlParameter<Guid> { TypedValue = id }
        );
        Assert.Equal(name, await command.ExecuteScalarAsync());
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
        await using var connection = new NpgsqlConnection(database.ApiConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM dictionaries.positions",
            connection
        );

        var error = await Assert.ThrowsAsync<PostgresException>(command.ExecuteScalarAsync);

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }

    private async Task<Guid> CreateAsync(string name)
    {
        await using var connection = new NpgsqlConnection(database.ApiConnectionString);
        await connection.OpenAsync();
        await using var command = Command(
            connection,
            "CALL dictionaries_app.position_create($1, NULL, NULL)",
            new NpgsqlParameter<string> { TypedValue = name }
        );
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    private async Task<List<string>> ListNamesAsync(string search)
    {
        await using var connection = new NpgsqlConnection(database.ApiConnectionString);
        await connection.OpenAsync();
        await using var command = Command(
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

    private static NpgsqlCommand Command(
        NpgsqlConnection connection,
        string sql,
        params NpgsqlParameter[] parameters
    )
    {
        var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        return command;
    }
}
