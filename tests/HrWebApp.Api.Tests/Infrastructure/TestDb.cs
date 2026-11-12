using Npgsql;

namespace HrWebApp.Api.Tests.Infrastructure;

/// <summary>Połączenia do testów rutyn: jako rola API z aktorem albo jako superużytkownik do aranżacji.</summary>
public static class TestDb
{
    public static async Task<NpgsqlConnection> ApiAsync(
        DatabaseFixture database,
        Guid? actorId = null
    )
    {
        var connection = new NpgsqlConnection(database.ApiConnectionString);
        await connection.OpenAsync();
        await using var command = Command(
            connection,
            "SELECT set_config('hr.actor_id', $1, false)",
            new NpgsqlParameter<string>
            {
                TypedValue = actorId is null || actorId == Guid.Empty ? "" : actorId.ToString(),
            }
        );
        await command.ExecuteNonQueryAsync();
        return connection;
    }

    public static async Task<T?> AdminScalarAsync<T>(
        DatabaseFixture database,
        string sql,
        params NpgsqlParameter[] parameters
    )
    {
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = Command(connection, sql, parameters);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)value;
    }

    /// <summary>Id konta deweloperskiego (admin, kadry, pracownik).</summary>
    public static async Task<Guid> DevUserAsync(DatabaseFixture database, string login) =>
        await AdminScalarAsync<Guid>(
            database,
            "SELECT id FROM platform.users WHERE login = $1",
            new NpgsqlParameter<string> { TypedValue = login }
        );

    /// <summary>Zakłada konto z hasłem i rolami bezpośrednio w tabeli (aranżacja testu).</summary>
    public static async Task<Guid> CreateUserAsync(
        DatabaseFixture database,
        string login,
        string password,
        params string[] roles
    )
    {
        var id = await AdminScalarAsync<Guid>(
            database,
            """
            INSERT INTO platform.users (login, email, display_name, password_hash)
            VALUES ($1, $1 || '@test.example', $1, ext.crypt($2, ext.gen_salt('bf', 4)))
            RETURNING id
            """,
            new NpgsqlParameter<string> { TypedValue = login },
            new NpgsqlParameter<string> { TypedValue = password }
        );
        foreach (var role in roles)
        {
            await AdminScalarAsync<object>(
                database,
                "INSERT INTO platform.user_roles (user_id, role) VALUES ($1, $2)",
                new NpgsqlParameter<Guid> { TypedValue = id },
                new NpgsqlParameter<string> { TypedValue = role }
            );
        }

        return id;
    }

    /// <summary>Zakłada urząd skarbowy z wolnym kodem z zakresu 9000–9999 (poza wykazem urzędów).</summary>
    public static async Task<Guid> TaxOfficeAsync(DatabaseFixture database, bool isActive = true) =>
        await AdminScalarAsync<Guid>(
            database,
            """
            INSERT INTO dictionaries.tax_offices (code, name, is_active)
            SELECT c.code, 'Urząd Testowy ' || c.code, $1
            FROM (
                SELECT lpad(n::text, 4, '0') AS code FROM generate_series(9000, 9999) n
                EXCEPT SELECT code FROM dictionaries.tax_offices
                ORDER BY 1 LIMIT 1
            ) c
            RETURNING id
            """,
            new NpgsqlParameter<bool> { TypedValue = isActive }
        );

    public static NpgsqlCommand Command(
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
