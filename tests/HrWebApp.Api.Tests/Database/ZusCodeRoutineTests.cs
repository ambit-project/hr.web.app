using HrWebApp.Api.Tests.Infrastructure;
using Npgsql;

namespace HrWebApp.Api.Tests.Database;

[Collection(DatabaseCollection.Name)]
public sealed class ZusCodeRoutineTests(DatabaseFixture database)
{
    [Fact]
    public async Task Official_codes_are_loaded_by_migration()
    {
        Assert.True(await ValidAsync("title", "0110", new DateOnly(2026, 10, 1)));
        Assert.True(await ValidAsync("title", "0411", new DateOnly(2026, 10, 1)));
        Assert.True(await ValidAsync("pension", "0", new DateOnly(2026, 10, 1)));
        Assert.True(await ValidAsync("disability", "0", new DateOnly(2026, 10, 1)));
    }

    [Fact]
    public async Task Create_writes_audit_and_code_is_valid_only_inside_its_period()
    {
        var code = await FreeTitleCodeAsync();

        var id = await CreateAsync(
            "title",
            code,
            "Kod testowy",
            new(2026, 1, 1),
            new(2026, 12, 31)
        );

        Assert.False(await ValidAsync("title", code, new DateOnly(2025, 12, 31)));
        Assert.True(await ValidAsync("title", code, new DateOnly(2026, 1, 1)));
        Assert.True(await ValidAsync("title", code, new DateOnly(2026, 12, 31)));
        Assert.False(await ValidAsync("title", code, new DateOnly(2027, 1, 1)));
        Assert.Equal(
            1L,
            await TestDb.AdminScalarAsync<long>(
                database,
                "SELECT count(*) FROM platform.audit_entries WHERE entity = 'zus_code' AND entity_id = $1",
                new NpgsqlParameter<string> { TypedValue = id.ToString() }
            )
        );
    }

    [Theory]
    [InlineData("title", "011", """{"code": ["invalid"]}""")]
    [InlineData("pension", "12", """{"code": ["invalid"]}""")]
    [InlineData("other", "1", """{"kind": ["invalid"]}""")]
    [InlineData("title", "0110", """{"code": ["duplicate"]}""")]
    public async Task Create_checks_code_format_for_kind_and_uniqueness(
        string kind,
        string code,
        string hint
    )
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            CreateAsync(kind, code, "Kod", new(2026, 1, 1), null)
        );

        Assert.Equal("U0001", error.SqlState);
        Assert.Equal(hint, error.Hint);
    }

    [Fact]
    public async Task End_before_start_is_rejected()
    {
        var error = await Assert.ThrowsAsync<PostgresException>(async () =>
            await CreateAsync(
                "title",
                await FreeTitleCodeAsync(),
                "Kod",
                new(2026, 2, 1),
                new(2026, 1, 31)
            )
        );

        Assert.Equal("""{"validTo": ["beforeFrom"]}""", error.Hint);
    }

    [Fact]
    public async Task Update_changes_name_and_period_with_row_version()
    {
        var id = await CreateAsync(
            "title",
            await FreeTitleCodeAsync(),
            "Stara nazwa",
            new(2026, 1, 1),
            null
        );
        var version = await RowVersionAsync(id);

        await UpdateAsync(id, version, "Nowa nazwa", new(2026, 1, 1), new(2026, 6, 30));
        var stale = await Assert.ThrowsAsync<PostgresException>(() =>
            UpdateAsync(id, version, "Jeszcze inna", new(2026, 1, 1), null)
        );

        Assert.Equal("U0003", stale.SqlState);
        Assert.Equal(
            "Nowa nazwa|2026-06-30",
            await TestDb.AdminScalarAsync<string>(
                database,
                "SELECT name || '|' || valid_to FROM dictionaries.zus_codes WHERE id = $1",
                new NpgsqlParameter<Guid> { TypedValue = id }
            )
        );
    }

    [Fact]
    public async Task Kadry_read_only_administrator_writes()
    {
        var create = await Assert.ThrowsAsync<PostgresException>(async () =>
            await CreateAsync(
                "title",
                await FreeTitleCodeAsync(),
                "Kod",
                new(2026, 1, 1),
                null,
                "kadry"
            )
        );
        await using var connection = await TestDb.ApiAsync(
            database,
            await TestDb.DevUserAsync(database, "kadry")
        );
        await using var list = TestDb.Command(
            connection,
            "SELECT count(*) FROM dictionaries_app.zus_code_list('title', '0110', DATE '2026-10-01')"
        );

        Assert.Equal("U0004", create.SqlState);
        Assert.Equal(1L, (long)(await list.ExecuteScalarAsync())!);
    }

    private async Task<string> FreeTitleCodeAsync() =>
        (
            await TestDb.AdminScalarAsync<string>(
                database,
                """
                SELECT lpad(n::text, 4, '0') FROM generate_series(9000, 9999) n
                EXCEPT SELECT code FROM dictionaries.zus_codes WHERE kind = 'title'
                ORDER BY 1 LIMIT 1
                """
            )
        )!;

    private async Task<bool> ValidAsync(string kind, string code, DateOnly on) =>
        await TestDb.AdminScalarAsync<bool>(
            database,
            "SELECT dictionaries.zus_code_valid($1, $2, $3)",
            new NpgsqlParameter<string> { TypedValue = kind },
            new NpgsqlParameter<string> { TypedValue = code },
            new NpgsqlParameter<DateOnly> { TypedValue = on }
        );

    private async Task<long> RowVersionAsync(Guid id) =>
        await TestDb.AdminScalarAsync<long>(
            database,
            "SELECT xmin::text::bigint FROM dictionaries.zus_codes WHERE id = $1",
            new NpgsqlParameter<Guid> { TypedValue = id }
        );

    private async Task<Guid> CreateAsync(
        string kind,
        string code,
        string name,
        DateOnly from,
        DateOnly? to,
        string actor = "admin"
    )
    {
        await using var connection = await TestDb.ApiAsync(
            database,
            await TestDb.DevUserAsync(database, actor)
        );
        await using var command = TestDb.Command(
            connection,
            "CALL dictionaries_app.zus_code_create($1, $2, $3, $4, $5, NULL)",
            new NpgsqlParameter<string> { TypedValue = kind },
            new NpgsqlParameter<string> { TypedValue = code },
            new NpgsqlParameter<string> { TypedValue = name },
            new NpgsqlParameter<DateOnly> { TypedValue = from },
            new NpgsqlParameter<DateOnly?> { TypedValue = to }
        );
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    private async Task UpdateAsync(Guid id, long version, string name, DateOnly from, DateOnly? to)
    {
        await using var connection = await TestDb.ApiAsync(
            database,
            await TestDb.DevUserAsync(database, "admin")
        );
        await using var command = TestDb.Command(
            connection,
            "CALL dictionaries_app.zus_code_update($1, $2, $3, $4, $5)",
            new NpgsqlParameter<Guid> { TypedValue = id },
            new NpgsqlParameter<long> { TypedValue = version },
            new NpgsqlParameter<string> { TypedValue = name },
            new NpgsqlParameter<DateOnly> { TypedValue = from },
            new NpgsqlParameter<DateOnly?> { TypedValue = to }
        );
        await command.ExecuteNonQueryAsync();
    }
}
