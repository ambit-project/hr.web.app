using HrWebApp.Api.Tests.Infrastructure;
using Npgsql;

namespace HrWebApp.Api.Tests.Database;

[Collection(DatabaseCollection.Name)]
public sealed class CompanyRoutineTests(DatabaseFixture database)
{
    [Fact]
    public async Task Create_normalizes_nip_and_regon_and_writes_audit()
    {
        var nip = FakeIds.Nip();
        var formatted = $"{nip[..3]}-{nip[3..6]}-{nip[6..8]}-{nip[8..]}";

        var id = await CreateAsync(Unique.Login(), formatted, FakeIds.Regon());

        Assert.Equal(
            nip,
            await TestDb.AdminScalarAsync<string>(
                database,
                "SELECT nip FROM platform.companies WHERE id = $1",
                new NpgsqlParameter<Guid> { TypedValue = id }
            )
        );
        Assert.Equal(
            1L,
            await TestDb.AdminScalarAsync<long>(
                database,
                "SELECT count(*) FROM platform.audit_entries WHERE entity = 'company' AND entity_id = $1",
                new NpgsqlParameter<string> { TypedValue = id.ToString() }
            )
        );
    }

    [Fact]
    public async Task Create_reports_all_invalid_fields_at_once()
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            CreateAsync("", FakeIds.Broken(FakeIds.Nip()), FakeIds.Broken(FakeIds.Regon()), "00000")
        );

        Assert.Equal("U0001", error.SqlState);
        Assert.Contains("\"nip\": [\"invalid\"]", error.Hint);
        Assert.Contains("\"regon\": [\"invalid\"]", error.Hint);
        Assert.Contains("\"shortName\": [\"required\"]", error.Hint);
        Assert.Contains("\"postalCode\": [\"invalid\"]", error.Hint);
    }

    [Fact]
    public async Task Create_rejects_duplicate_nip_and_short_name()
    {
        var shortName = Unique.Login();
        var nip = FakeIds.Nip();
        await CreateAsync(shortName, nip, FakeIds.Regon());

        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            CreateAsync(shortName.ToUpperInvariant(), nip, FakeIds.Regon())
        );

        Assert.Contains("\"nip\": [\"duplicate\"]", error.Hint);
        Assert.Contains("\"shortName\": [\"duplicate\"]", error.Hint);
    }

    [Fact]
    public async Task Only_administrator_creates()
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            CreateAsync(Unique.Login(), FakeIds.Nip(), FakeIds.Regon(), actor: "kadry")
        );

        Assert.Equal("U0004", error.SqlState);
    }

    [Fact]
    public async Task Update_with_stale_row_version_is_a_conflict()
    {
        var id = await CreateAsync(Unique.Login(), FakeIds.Nip(), FakeIds.Regon());
        var version = await RowVersionAsync(id);
        await UpdateAsync(id, version, "Nowa nazwa");

        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            UpdateAsync(id, version, "Druga zmiana")
        );

        Assert.Equal("U0003", error.SqlState);
    }

    [Fact]
    public async Task Update_of_missing_company_is_not_found()
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            UpdateAsync(Guid.NewGuid(), 1, "X")
        );

        Assert.Equal("U0002", error.SqlState);
    }

    private async Task<Guid> CreateAsync(
        string shortName,
        string nip,
        string regon,
        string postalCode = "00-001",
        string actor = "admin"
    )
    {
        await using var connection = await TestDb.ApiAsync(
            database,
            await TestDb.DevUserAsync(database, actor)
        );
        await using var command = TestDb.Command(
            connection,
            "CALL platform_app.company_create($1, 'Spółka Testowa sp. z o.o.', $2, $3, 'ul. Testowa 1', $4, 'Przykładowo', NULL)",
            new NpgsqlParameter<string> { TypedValue = shortName },
            new NpgsqlParameter<string> { TypedValue = nip },
            new NpgsqlParameter<string> { TypedValue = regon },
            new NpgsqlParameter<string> { TypedValue = postalCode }
        );
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    private async Task<long> RowVersionAsync(Guid id) =>
        await TestDb.AdminScalarAsync<long>(
            database,
            "SELECT xmin::text::bigint FROM platform.companies WHERE id = $1",
            new NpgsqlParameter<Guid> { TypedValue = id }
        );

    private async Task UpdateAsync(Guid id, long version, string name)
    {
        var current = await TestDb.AdminScalarAsync<string>(
            database,
            "SELECT short_name || '|' || nip || '|' || regon FROM platform.companies WHERE id = $1",
            new NpgsqlParameter<Guid> { TypedValue = id }
        );
        var parts = (current ?? "x|" + FakeIds.Nip() + "|" + FakeIds.Regon()).Split('|');
        await using var connection = await TestDb.ApiAsync(
            database,
            await TestDb.DevUserAsync(database, "admin")
        );
        await using var command = TestDb.Command(
            connection,
            "CALL platform_app.company_update($1, $2, $3, $4, $5, $6, 'ul. Nowa 2', '00-002', 'Przykładowo', true)",
            new NpgsqlParameter<Guid> { TypedValue = id },
            new NpgsqlParameter<long> { TypedValue = version },
            new NpgsqlParameter<string> { TypedValue = parts[0] },
            new NpgsqlParameter<string> { TypedValue = name },
            new NpgsqlParameter<string> { TypedValue = parts[1] },
            new NpgsqlParameter<string> { TypedValue = parts[2] }
        );
        await command.ExecuteNonQueryAsync();
    }
}
