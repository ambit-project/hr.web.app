using System.Net.Http.Headers;
using HrWebApp.Api.Features.Optima.Agent;
using HrWebApp.Api.Tests.Infrastructure;
using Npgsql;

namespace HrWebApp.Api.Tests.Features.Optima;

/// <summary>Aranżacja testów integracji z Optimą wprost w tabelach (superużytkownik).</summary>
public static class OptimaTestData
{
    /// <summary>Nowy klucz agenta; zwraca token (baza dostaje tylko skrót, jak przy zakładaniu przez API).</summary>
    public static async Task<string> AgentKeyAsync(DatabaseFixture database)
    {
        var token = AgentToken.New();
        await TestDb.AdminScalarAsync<Guid>(
            database,
            "INSERT INTO optima.agent_keys (name, key_hash, key_prefix) VALUES ('test', $1, $2) RETURNING id",
            new NpgsqlParameter<byte[]> { TypedValue = AgentToken.Hash(token) },
            new NpgsqlParameter<string> { TypedValue = AgentToken.DisplayPrefix(token) }
        );
        return token;
    }

    public static HttpClient AgentClient(DatabaseFixture database, string token)
    {
        var client = database.Api.CreateClient();
        client.DefaultRequestHeaders.Add(AgentKeyAuthenticationHandler.HeaderName, token);
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json")
        );
        return client;
    }

    /// <summary>
    /// Testy z bazą idą po kolei w jednej kolekcji, ale kolejka operacji jest wspólna: zamyka oczekujące operacje
    /// poprzednich testów, żeby claim zwrócił operację tego testu.
    /// </summary>
    public static async Task ClearQueueAsync(DatabaseFixture database) =>
        await TestDb.AdminScalarAsync<object>(
            database,
            "UPDATE optima.operations SET state = 'failed', finished_at = now() WHERE state IN ('pending', 'claimed')"
        );

    /// <summary>Spółka powiązana z bazą Optimy o unikalnej nazwie.</summary>
    public static async Task<(Guid Company, string Database, string Nip)> LinkedCompanyAsync(
        DatabaseFixture database
    )
    {
        var company = await TestDb.CompanyAsync(database);
        var nip = await TestDb.AdminScalarAsync<string>(
            database,
            "SELECT nip FROM platform.companies WHERE id = $1",
            new NpgsqlParameter<Guid> { TypedValue = company }
        );
        var name = "CDN_" + Unique.Code();
        await TestDb.AdminScalarAsync<object>(
            database,
            "INSERT INTO optima.databases (database_name, tax_number) VALUES ($1, $2)",
            new NpgsqlParameter<string> { TypedValue = name },
            new NpgsqlParameter<string> { TypedValue = nip! }
        );
        await TestDb.AdminScalarAsync<object>(
            database,
            "INSERT INTO optima.company_links (company_id, database_name) VALUES ($1, $2)",
            new NpgsqlParameter<Guid> { TypedValue = company },
            new NpgsqlParameter<string> { TypedValue = name }
        );
        return (company, name, nip!);
    }

    /// <summary>Kartoteka z PESEL, dokumentem i umową od 2026-01-01 bez końca.</summary>
    public static async Task<Guid> EmployeeAsync(
        DatabaseFixture database,
        Guid company,
        string lastName = "Kowalski",
        string? document = "ABC123456"
    )
    {
        var pesel = FakeIds.Pesel(new DateOnly(1985, 1, 1));
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        await connection.OpenAsync();
        await using (
            var key = TestDb.Command(
                connection,
                "SELECT set_config('hr.data_key', $1, false)",
                new NpgsqlParameter<string> { TypedValue = TestDb.DataKey }
            )
        )
        {
            await key.ExecuteNonQueryAsync();
        }

        await using var command = TestDb.Command(
            connection,
            """
            WITH e AS (
                INSERT INTO employees.employees (company_id, number, first_name, last_name, birth_date, pesel_enc,
                                                 pesel_hmac, identity_document_enc)
                VALUES ($1, left(md5(random()::text), 10), 'Jan', $2, DATE '1985-01-01', employees.encrypt($3),
                        employees.pesel_hash($3), employees.encrypt($4))
                RETURNING id
            ), m AS (
                INSERT INTO employment.employments (employee_id, company_id, number, kind, contract_type, signed_at,
                                                    start_date)
                SELECT e.id, $1, gen_random_uuid()::text, 'employment', 'indefinite', DATE '2026-01-01',
                       DATE '2026-01-01'
                FROM e
                RETURNING id
            )
            SELECT e.id FROM e, m
            """,
            new NpgsqlParameter<Guid> { TypedValue = company },
            new NpgsqlParameter<string> { TypedValue = lastName },
            new NpgsqlParameter<string> { TypedValue = pesel },
            new NpgsqlParameter<string?> { TypedValue = document }
        );
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    public static async Task<Guid> EmploymentOfAsync(DatabaseFixture database, Guid employee) =>
        await TestDb.AdminScalarAsync<Guid>(
            database,
            "SELECT id FROM employment.employments WHERE employee_id = $1",
            new NpgsqlParameter<Guid> { TypedValue = employee }
        );

    /// <summary>Składnik słownika (dodatek albo potrącenie zaliczki) o danym sposobie liczenia.</summary>
    public static async Task<Guid> ComponentTypeAsync(
        DatabaseFixture database,
        string kind,
        string calculation = "fixed"
    ) =>
        await TestDb.AdminScalarAsync<Guid>(
            database,
            """
            INSERT INTO pay_components.component_types (code, name, kind, calculation, in_zus_base, in_health_base,
                                                        taxable, in_sickness_base, deduction_category)
            SELECT $1, 'Składnik ' || $1, $2, $3,
                   CASE WHEN $2 = 'addition' THEN true END, CASE WHEN $2 = 'addition' THEN true END,
                   CASE WHEN $2 = 'addition' THEN true END, CASE WHEN $2 = 'addition' THEN true END,
                   CASE WHEN $2 = 'deduction' THEN 'advance' END
            RETURNING id
            """,
            new NpgsqlParameter<string> { TypedValue = Unique.Code() },
            new NpgsqlParameter<string> { TypedValue = kind },
            new NpgsqlParameter<string> { TypedValue = calculation }
        );

    public static async Task<Guid> AssignAsync(
        DatabaseFixture database,
        Guid employment,
        Guid componentType,
        decimal value,
        DateOnly from,
        DateOnly? to = null
    ) =>
        await TestDb.AdminScalarAsync<Guid>(
            database,
            """
            INSERT INTO pay_components.employment_components (employment_id, component_type_id, value, valid_from,
                                                              valid_to)
            VALUES ($1, $2, $3, $4, $5) RETURNING id
            """,
            new NpgsqlParameter<Guid> { TypedValue = employment },
            new NpgsqlParameter<Guid> { TypedValue = componentType },
            new NpgsqlParameter<decimal> { TypedValue = value },
            new NpgsqlParameter<DateOnly> { TypedValue = from },
            new NpgsqlParameter<DateOnly?> { TypedValue = to }
        );

    /// <summary>Rodzaj płacy Optimy w bazie, opcjonalnie powiązany ze składnikiem.</summary>
    public static async Task PayTypeAsync(
        DatabaseFixture database,
        string databaseName,
        int optimaId,
        bool isDeduction,
        Guid? componentType = null,
        bool isActive = true
    ) =>
        await TestDb.AdminScalarAsync<object>(
            database,
            """
            INSERT INTO optima.pay_types (database_name, optima_id, name, is_deduction, is_pkzp, is_seizure,
                                          is_active, is_withdrawn, component_type_id)
            VALUES ($1, $2, 'Rodzaj ' || $2, $3, false, false, $5, false, $4)
            """,
            new NpgsqlParameter<string> { TypedValue = databaseName },
            new NpgsqlParameter<int> { TypedValue = optimaId },
            new NpgsqlParameter<bool> { TypedValue = isDeduction },
            new NpgsqlParameter<Guid?> { TypedValue = componentType },
            new NpgsqlParameter<bool> { TypedValue = isActive }
        );

    /// <summary>Operacja wstawiona wprost (oczekująca), np. do testów kolejki.</summary>
    public static async Task<Guid> PendingOperationAsync(
        DatabaseFixture database,
        string kind = "ReadPayrollComponents"
    ) =>
        await TestDb.AdminScalarAsync<Guid>(
            database,
            "INSERT INTO optima.operations (kind) VALUES ($1) RETURNING id",
            new NpgsqlParameter<string> { TypedValue = kind }
        );
}
