using HrWebApp.Api.Tests.Infrastructure;
using Npgsql;

namespace HrWebApp.Api.Tests.Database;

[Collection(DatabaseCollection.Name)]
public sealed class EmployeeCopyRoutineTests(DatabaseFixture database)
{
    [Fact]
    public async Task Copies_personal_data_address_bank_and_link_and_skips_duplicates()
    {
        var source = await TestDb.CompanyAsync(database);
        var target = await TestDb.CompanyAsync(database);
        var pesel = FakeIds.Pesel(new DateOnly(1985, 1, 1));
        var withPesel = await EmployeeAsync(source, "Jan", pesel);
        var noPesel = await EmployeeAsync(source, "Ewa", null);
        await AdminAsync<object>(
            """
            INSERT INTO employees.addresses (employee_id, kind, street, postal_code, city, valid_from)
            VALUES ($1, 'residence', 'ul. Polna 1', '00-001', 'Warszawa', DATE '2020-01-01')
            """,
            withPesel
        );
        await AdminAsync<object>(
            """
            INSERT INTO employees.bank_accounts (employee_id, iban_enc, iban_last4, valid_from)
            VALUES ($1, '\x00'::bytea, '1234', DATE '2020-01-01')
            """,
            withPesel
        );
        var user = await TestDb.CreateUserAsync(
            database,
            Unique.Login(),
            "haslo-testowe-123",
            "pracownik"
        );
        await AdminAsync<object>(
            "INSERT INTO platform.user_employee_links (user_id, employee_id, company_id) VALUES ($1, $2, $3)",
            user,
            withPesel,
            source
        );

        var first = await CopyAsync(target, [withPesel, noPesel]);
        var second = await CopyAsync(target, [withPesel, noPesel]);
        var same = await CopyAsync(source, [withPesel]);

        Assert.All(first, row => Assert.Null(row.Reason));
        var copy = first.Single(row => row.Source == withPesel).New!.Value;
        Assert.Equal(
            "Warszawa|1234|1",
            await AdminAsync<string>(
                """
                SELECT a.city || '|' || b.iban_last4 || '|' || count(l.*)
                FROM employees.addresses a, employees.bank_accounts b, platform.user_employee_links l
                WHERE a.employee_id = $1 AND b.employee_id = $1 AND l.employee_id = $1
                GROUP BY a.city, b.iban_last4
                """,
                copy
            )
        );
        Assert.Equal(
            ["personExists", "peselExists"],
            second.OrderBy(row => row.Reason).Select(row => row.Reason)
        );
        Assert.Equal("sameCompany", Assert.Single(same).Reason);
    }

    [Fact]
    public async Task Copies_by_filter_and_rejects_inactive_target()
    {
        var source = await TestDb.CompanyAsync(database);
        var target = await TestDb.CompanyAsync(database);
        await EmployeeAsync(source, "Adam", null);
        await EmployeeAsync(source, "Ola", null);

        var copied = await CopyAsync(target, [], source);
        await AdminAsync<object>(
            "UPDATE platform.companies SET is_active = false WHERE id = $1",
            target
        );
        var inactive = await Assert.ThrowsAsync<PostgresException>(() =>
            CopyAsync(target, [], source)
        );

        Assert.Equal(2, copied.Count);
        Assert.Equal("""{"targetCompanyId": ["notFound"]}""", inactive.Hint);
    }

    private sealed record Row(Guid Source, Guid? New, string? Reason);

    private async Task<List<Row>> CopyAsync(Guid target, Guid[] ids, Guid? company = null)
    {
        await using var connection = await TestDb.ApiAsync(
            database,
            await TestDb.DevUserAsync(database, "kadry")
        );
        await using var command = TestDb.Command(
            connection,
            "SELECT source_id, new_id, skipped_reason FROM employees_app.employee_copy($1, $2, $3, NULL, NULL)"
        );
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = target });
        command.Parameters.Add(new NpgsqlParameter<Guid[]> { TypedValue = ids });
        command.Parameters.Add(
            company is null
                ? new NpgsqlParameter
                {
                    Value = DBNull.Value,
                    NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Uuid,
                }
                : new NpgsqlParameter<Guid> { TypedValue = company.Value }
        );
        var rows = new List<Row>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(
                new Row(
                    reader.GetGuid(0),
                    reader.IsDBNull(1) ? null : reader.GetGuid(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2)
                )
            );
        }
        return rows;
    }

    private async Task<Guid> EmployeeAsync(Guid company, string firstName, string? pesel)
    {
        // Wstawienie wprost jako superużytkownik; szyfrowanie wymaga klucza w ustawieniu sesji (ADR-0009).
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        await connection.OpenAsync();
        await using (
            var key = TestDb.Command(
                connection,
                $"SELECT set_config('hr.data_key', '{TestDb.DataKey}', false)"
            )
        )
        {
            await key.ExecuteNonQueryAsync();
        }
        await using var command = TestDb.Command(
            connection,
            """
            INSERT INTO employees.employees (company_id, number, first_name, last_name, birth_date, pesel_enc, pesel_hmac)
            SELECT $1, left(md5(random()::text), 10), $2, 'Kowalski', DATE '1985-01-01',
                   employees.encrypt($3), employees.pesel_hash($3)
            RETURNING id
            """
        );
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = company });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = firstName });
        command.Parameters.Add(
            new NpgsqlParameter
            {
                Value = (object?)pesel ?? DBNull.Value,
                NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text,
            }
        );
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    private async Task<T?> AdminAsync<T>(string sql, params object?[] args)
    {
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = TestDb.Command(connection, sql);
        foreach (var arg in args)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = arg ?? DBNull.Value });
        }
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)value;
    }
}
