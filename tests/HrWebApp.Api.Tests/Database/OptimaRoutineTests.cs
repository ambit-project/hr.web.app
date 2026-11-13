using HrWebApp.Api.Features.Optima.Agent;
using HrWebApp.Api.Tests.Features.Optima;
using HrWebApp.Api.Tests.Infrastructure;
using Npgsql;

namespace HrWebApp.Api.Tests.Database;

[Collection(DatabaseCollection.Name)]
public sealed class OptimaRoutineTests(DatabaseFixture database)
{
    [Fact]
    public async Task Two_parallel_claims_never_get_the_same_operation()
    {
        await OptimaTestData.ClearQueueAsync(database);
        var hash = AgentToken.Hash(await OptimaTestData.AgentKeyAsync(database));
        var operation = await OptimaTestData.PendingOperationAsync(database);

        var claims = await Task.WhenAll(ClaimAsync(hash), ClaimAsync(hash));

        Assert.Single(claims, c => c == operation);
        Assert.Single(claims, c => c is null);
    }

    [Fact]
    public async Task Empty_claim_writes_no_audit_and_last_seen_at_most_once_a_minute()
    {
        await OptimaTestData.ClearQueueAsync(database);
        var hash = AgentToken.Hash(await OptimaTestData.AgentKeyAsync(database));
        var audits = await CountAsync("SELECT count(*) FROM platform.audit_entries");

        Assert.Null(await ClaimAsync(hash));
        var seen = await TestDb.AdminScalarAsync<DateTime>(
            database,
            "SELECT last_seen_at FROM optima.agent_keys WHERE key_hash = $1",
            new NpgsqlParameter<byte[]> { TypedValue = hash }
        );
        Assert.Null(await ClaimAsync(hash));

        Assert.Equal(audits, await CountAsync("SELECT count(*) FROM platform.audit_entries"));
        Assert.Equal(
            seen,
            await TestDb.AdminScalarAsync<DateTime>(
                database,
                "SELECT last_seen_at FROM optima.agent_keys WHERE key_hash = $1",
                new NpgsqlParameter<byte[]> { TypedValue = hash }
            )
        );
    }

    [Fact]
    public async Task Claim_offers_only_supported_kinds()
    {
        await OptimaTestData.ClearQueueAsync(database);
        var hash = AgentToken.Hash(await OptimaTestData.AgentKeyAsync(database));
        await OptimaTestData.PendingOperationAsync(database);

        Assert.Null(await ClaimAsync(hash, "ReadPayrolls"));
    }

    [Fact]
    public async Task Result_for_an_operation_held_by_another_key_is_not_found()
    {
        await OptimaTestData.ClearQueueAsync(database);
        var holder = AgentToken.Hash(await OptimaTestData.AgentKeyAsync(database));
        var other = AgentToken.Hash(await OptimaTestData.AgentKeyAsync(database));
        var operation = await OptimaTestData.PendingOperationAsync(database);
        Assert.Equal(operation, await ClaimAsync(holder));

        await using var connection = await TestDb.ApiAsync(database);
        await using var command = TestDb.Command(
            connection,
            "CALL optima_app.agent_result($1, $2, 1, true, NULL, '[]', NULL, NULL)",
            new NpgsqlParameter<byte[]> { TypedValue = other },
            new NpgsqlParameter<Guid> { TypedValue = operation }
        );
        var error = await Assert.ThrowsAsync<PostgresException>(command.ExecuteNonQueryAsync);

        Assert.Equal("U0002", error.SqlState);
    }

    [Fact]
    public async Task Pay_type_missing_from_a_later_read_is_marked_and_keeps_its_mapping()
    {
        var (company, databaseName, _) = await OptimaTestData.LinkedCompanyAsync(database);
        var component = await OptimaTestData.ComponentTypeAsync(database, "deduction");
        await OptimaTestData.PayTypeAsync(database, databaseName, 338, true, component);
        await OptimaTestData.PayTypeAsync(database, databaseName, 12, false);

        await StorePayTypesAsync(
            databaseName,
            """[{"optimaId": 12, "name": "Premia", "isDeduction": false}]"""
        );

        Assert.Equal(
            1L,
            await CountAsync(
                $"SELECT count(*) FROM optima.pay_types WHERE database_name = '{databaseName}' AND optima_id = 338 AND missing_since IS NOT NULL AND component_type_id IS NOT NULL"
            )
        );

        // Wraca jako dodatek: zmienił rodzaj, więc powiązanie z potrąceniem przepada.
        await StorePayTypesAsync(
            databaseName,
            """[{"optimaId": 338, "name": "Teraz dodatek", "isDeduction": false}]"""
        );
        Assert.Equal(
            1L,
            await CountAsync(
                $"SELECT count(*) FROM optima.pay_types WHERE database_name = '{databaseName}' AND optima_id = 338 AND missing_since IS NULL AND component_type_id IS NULL"
            )
        );
        Assert.NotEqual(Guid.Empty, company);
    }

    [Fact]
    public async Task Mapping_a_deduction_pay_type_to_an_allowance_is_refused()
    {
        var (company, databaseName, _) = await OptimaTestData.LinkedCompanyAsync(database);
        var allowance = await OptimaTestData.ComponentTypeAsync(database, "addition");
        await OptimaTestData.PayTypeAsync(database, databaseName, 338, true);

        await using var connection = await TestDb.ApiAsync(
            database,
            await TestDb.DevUserAsync(database, "kadry")
        );
        await using var command = TestDb.Command(
            connection,
            "CALL optima_app.pay_type_map($1, 338, $2)",
            new NpgsqlParameter<Guid> { TypedValue = company },
            new NpgsqlParameter<Guid> { TypedValue = allowance }
        );
        var error = await Assert.ThrowsAsync<PostgresException>(command.ExecuteNonQueryAsync);

        Assert.Equal("U0001", error.SqlState);
        Assert.Contains("kindMismatch", error.Hint);
    }

    [Fact]
    public async Task Operation_for_a_company_whose_database_reports_another_tax_number_is_a_conflict()
    {
        var (company, databaseName, _) = await OptimaTestData.LinkedCompanyAsync(database);
        await TestDb.AdminScalarAsync<object>(
            database,
            "UPDATE optima.databases SET tax_number = '0000000000' WHERE database_name = $1",
            new NpgsqlParameter<string> { TypedValue = databaseName }
        );

        await using var connection = await TestDb.ApiAsync(
            database,
            await TestDb.DevUserAsync(database, "kadry")
        );
        await using var command = TestDb.Command(
            connection,
            "CALL optima_app.operation_create('ReadPayrolls', $1, 2026, 10, NULL, NULL)",
            new NpgsqlParameter<Guid> { TypedValue = company }
        );
        var error = await Assert.ThrowsAsync<PostgresException>(command.ExecuteNonQueryAsync);

        Assert.Equal("U0003", error.SqlState);
        Assert.Equal("taxNumberMismatch", error.Hint);
    }

    private async Task<Guid?> ClaimAsync(byte[] hash, string kind = "ReadPayrollComponents")
    {
        await using var connection = await TestDb.ApiAsync(database);
        await using var command = TestDb.Command(
            connection,
            "CALL optima_app.agent_claim($1, '0.5.0.0', $2, NULL, NULL, NULL)",
            new NpgsqlParameter<byte[]> { TypedValue = hash },
            new NpgsqlParameter<string[]> { TypedValue = [kind] }
        );
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return reader.IsDBNull(0) ? null : reader.GetGuid(0);
    }

    private async Task StorePayTypesAsync(string databaseName, string components) =>
        await TestDb.AdminScalarAsync<object>(
            database,
            "SELECT optima.store_pay_types(jsonb_build_array(jsonb_build_object('databaseName', $1::text, 'components', $2::jsonb)))",
            new NpgsqlParameter<string> { TypedValue = databaseName },
            new NpgsqlParameter<string> { TypedValue = components }
        );

    private async Task<long> CountAsync(string sql) =>
        await TestDb.AdminScalarAsync<long>(database, sql);
}
