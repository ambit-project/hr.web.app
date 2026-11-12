using HrWebApp.Api.Tests.Infrastructure;
using Npgsql;

namespace HrWebApp.Api.Tests.Database;

[Collection(DatabaseCollection.Name)]
public sealed class TaxOfficeRoutineTests(DatabaseFixture database)
{
    [Fact]
    public async Task Official_list_is_loaded_by_migration()
    {
        Assert.True(
            await TestDb.AdminScalarAsync<long>(
                database,
                "SELECT count(*) FROM dictionaries.tax_offices WHERE code < '9000'"
            ) > 300
        );
    }

    [Fact]
    public async Task Search_matches_code_prefix_and_name_fragment_ignoring_case()
    {
        var code = await CreateAsync(await FreeCodeAsync(), "Urząd Skarbowy Testowo-Śródmieście");

        Assert.Contains(code, await SearchAsync(code[..3]));
        Assert.Contains(code, await SearchAsync("testowo-śRÓDmieście"));
    }

    [Theory]
    [InlineData("12a4", """{"code": ["invalid"]}""")]
    [InlineData("", """{"code": ["required"]}""")]
    public async Task Create_rejects_invalid_code(string code, string hint)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => CreateAsync(code, "Urząd"));

        Assert.Equal("U0001", error.SqlState);
        Assert.Equal(hint, error.Hint);
    }

    [Fact]
    public async Task Create_rejects_duplicate_code_and_kadry_cannot_write()
    {
        var code = await CreateAsync(await FreeCodeAsync(), "Urząd A");

        var duplicate = await Assert.ThrowsAsync<PostgresException>(() =>
            CreateAsync(code, "Urząd B")
        );
        var forbidden = await Assert.ThrowsAsync<PostgresException>(async () =>
            await CreateAsync(await FreeCodeAsync(), "Urząd C", "kadry")
        );

        Assert.Equal("""{"code": ["duplicate"]}""", duplicate.Hint);
        Assert.Equal("U0004", forbidden.SqlState);
    }

    [Fact]
    public async Task Deactivated_office_disappears_from_default_list_and_audit_is_written()
    {
        var code = await CreateAsync(await FreeCodeAsync(), "Urząd do wyłączenia");
        var id = await TestDb.AdminScalarAsync<Guid>(
            database,
            "SELECT id FROM dictionaries.tax_offices WHERE code = $1",
            new NpgsqlParameter<string> { TypedValue = code }
        );
        var version = await TestDb.AdminScalarAsync<long>(
            database,
            "SELECT xmin::text::bigint FROM dictionaries.tax_offices WHERE id = $1",
            new NpgsqlParameter<Guid> { TypedValue = id }
        );

        await using (var connection = await AdminConnectionAsync())
        {
            await using var command = TestDb.Command(
                connection,
                "CALL dictionaries_app.tax_office_update($1, $2, 'Urząd wyłączony', false)",
                new NpgsqlParameter<Guid> { TypedValue = id },
                new NpgsqlParameter<long> { TypedValue = version }
            );
            await command.ExecuteNonQueryAsync();
        }

        Assert.DoesNotContain(code, await SearchAsync(code));
        Assert.Equal(
            2L,
            await TestDb.AdminScalarAsync<long>(
                database,
                "SELECT count(*) FROM platform.audit_entries WHERE entity = 'tax_office' AND entity_id = $1",
                new NpgsqlParameter<string> { TypedValue = id.ToString() }
            )
        );
    }

    private async Task<NpgsqlConnection> AdminConnectionAsync(string actor = "admin") =>
        await TestDb.ApiAsync(database, await TestDb.DevUserAsync(database, actor));

    private async Task<string> FreeCodeAsync() =>
        (
            await TestDb.AdminScalarAsync<string>(
                database,
                """
                SELECT lpad(n::text, 4, '0') FROM generate_series(9000, 9999) n
                EXCEPT SELECT code FROM dictionaries.tax_offices
                ORDER BY 1 LIMIT 1
                """
            )
        )!;

    private async Task<string> CreateAsync(string code, string name, string actor = "admin")
    {
        await using var connection = await AdminConnectionAsync(actor);
        await using var command = TestDb.Command(
            connection,
            "CALL dictionaries_app.tax_office_create($1, $2, NULL)",
            new NpgsqlParameter<string> { TypedValue = code },
            new NpgsqlParameter<string> { TypedValue = name }
        );
        await command.ExecuteNonQueryAsync();
        return code;
    }

    private async Task<List<string>> SearchAsync(string search)
    {
        await using var connection = await AdminConnectionAsync("kadry");
        await using var command = TestDb.Command(
            connection,
            "SELECT code FROM dictionaries_app.tax_office_list($1, false, 100, 0)",
            new NpgsqlParameter<string> { TypedValue = search }
        );
        await using var reader = await command.ExecuteReaderAsync();
        var codes = new List<string>();
        while (await reader.ReadAsync())
        {
            codes.Add(reader.GetString(0));
        }

        return codes;
    }
}
