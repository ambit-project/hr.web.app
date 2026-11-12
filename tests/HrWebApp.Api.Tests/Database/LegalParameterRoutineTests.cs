using HrWebApp.Api.Tests.Infrastructure;
using Npgsql;

namespace HrWebApp.Api.Tests.Database;

[Collection(DatabaseCollection.Name)]
public sealed class LegalParameterRoutineTests(DatabaseFixture database)
{
    [Fact]
    public async Task Parameters_2026_are_loaded()
    {
        Assert.Equal(4806.00m, await ValueAsync("placa.minimalna", new DateOnly(2026, 6, 30)));
        Assert.Equal(
            0.0976m,
            await ValueAsync("zus.emerytalna.pracownik", new DateOnly(2026, 11, 1))
        );
        Assert.Equal(120000m, await ValueAsync("pit.skala.prog", new DateOnly(2026, 12, 31)));
    }

    [Fact]
    public async Task Missing_value_is_an_error_not_zero()
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            ValueAsync("placa.minimalna", new DateOnly(2025, 12, 31))
        );

        Assert.Equal("U0006", error.SqlState);
    }

    [Fact]
    public async Task New_value_closes_previous_period_and_same_day_corrects_it()
    {
        var code = Code();
        await AddAsync(code, 100m, new DateOnly(2026, 1, 1));
        await AddAsync(code, 110m, new DateOnly(2026, 7, 1));
        await AddAsync(code, 111m, new DateOnly(2026, 7, 1));

        Assert.Equal(100m, await ValueAsync(code, new DateOnly(2026, 6, 30)));
        Assert.Equal(111m, await ValueAsync(code, new DateOnly(2026, 7, 1)));
        Assert.Equal(
            new DateOnly(2026, 6, 30),
            await TestDb.AdminScalarAsync<DateOnly>(
                database,
                "SELECT valid_to FROM platform.legal_parameters WHERE code = $1 AND valid_from = '2026-01-01'",
                new NpgsqlParameter<string> { TypedValue = code }
            )
        );
        Assert.Equal(
            3L,
            await TestDb.AdminScalarAsync<long>(
                database,
                "SELECT count(*) FROM platform.audit_entries WHERE entity = 'legal_parameter' AND after ->> 'code' = $1",
                new NpgsqlParameter<string> { TypedValue = code }
            )
        );
    }

    [Fact]
    public async Task Value_before_current_period_is_rejected()
    {
        var code = Code();
        await AddAsync(code, 1m, new DateOnly(2026, 7, 1));

        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            AddAsync(code, 2m, new DateOnly(2026, 1, 1))
        );

        Assert.Equal("""{"validFrom": ["beforeCurrent"]}""", error.Hint);
    }

    [Fact]
    public async Task Only_administrator_adds_and_lists()
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            AddAsync(Code(), 1m, new DateOnly(2026, 1, 1), "kadry")
        );

        Assert.Equal("U0004", error.SqlState);
    }

    private static string Code() => $"test.p{Guid.NewGuid():N}"[..20];

    private async Task<decimal> ValueAsync(string code, DateOnly on) =>
        await TestDb.AdminScalarAsync<decimal>(
            database,
            "SELECT platform.legal_parameter($1, $2)",
            new NpgsqlParameter<string> { TypedValue = code },
            new NpgsqlParameter<DateOnly> { TypedValue = on }
        );

    private async Task AddAsync(string code, decimal value, DateOnly from, string actor = "admin")
    {
        await using var connection = await TestDb.ApiAsync(
            database,
            await TestDb.DevUserAsync(database, actor)
        );
        await using var command = TestDb.Command(
            connection,
            "CALL platform_app.legal_parameter_add($1, $2, $3, 'test', NULL)",
            new NpgsqlParameter<string> { TypedValue = code },
            new NpgsqlParameter<decimal> { TypedValue = value },
            new NpgsqlParameter<DateOnly> { TypedValue = from }
        );
        await command.ExecuteNonQueryAsync();
    }
}
