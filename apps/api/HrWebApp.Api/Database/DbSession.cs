using Npgsql;

namespace HrWebApp.Api.Database;

/// <summary>
/// Jedno połączenie na zakres żądania. Zapis tylko procedurą (CALL), odczyt funkcją <c>&lt;moduł&gt;_app</c>.
/// Aktor (id konta z sesji) trafia do ustawienia <c>hr.actor_id</c>, które czyta <c>platform.actor_id()</c>.
/// </summary>
public sealed class DbSession(NpgsqlDataSource dataSource) : IAsyncDisposable
{
    private NpgsqlConnection? _connection;

    /// <summary>Ustawia SessionAuthenticationHandler przed pierwszym zapytaniem żądania.</summary>
    public Guid? ActorId { get; set; }

    public async Task<T?> ScalarAsync<T>(
        string sql,
        IReadOnlyList<NpgsqlParameter> parameters,
        CancellationToken cancellationToken
    )
    {
        await using var command = await CreateCommandAsync(sql, parameters, cancellationToken);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? default : (T)value;
    }

    public async Task<List<T>> QueryAsync<T>(
        string sql,
        IReadOnlyList<NpgsqlParameter> parameters,
        Func<NpgsqlDataReader, T> map,
        CancellationToken cancellationToken
    )
    {
        await using var command = await CreateCommandAsync(sql, parameters, cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<T>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(map(reader));
        }

        return rows;
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }

    private async Task<NpgsqlCommand> CreateCommandAsync(
        string sql,
        IReadOnlyList<NpgsqlParameter> parameters,
        CancellationToken cancellationToken
    )
    {
        var connection = await GetConnectionAsync(cancellationToken);
        var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }
        return command;
    }

    private async Task<NpgsqlConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is not null)
        {
            return _connection;
        }

        _connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT set_config('hr.actor_id', $1, false)",
            _connection
        );
        command.Parameters.Add(Db.Param(ActorId?.ToString() ?? string.Empty));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return _connection;
    }
}
