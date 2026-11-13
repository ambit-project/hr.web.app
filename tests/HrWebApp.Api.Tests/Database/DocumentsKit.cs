using System.Text.Json;
using HrWebApp.Api.Tests.Infrastructure;
using Npgsql;

namespace HrWebApp.Api.Tests.Database;

/// <summary>
/// Aranżacja testów dokumentów (010): kartoteki z PESEL, adresem zamieszkania i urzędem skarbowym, odczyt wierszy
/// rutyn kontraktu jako wybrane konto i pozycje PIT-11 z <c>data</c>.
/// </summary>
public sealed class DocumentsKit(DatabaseFixture database)
{
    public PayrollKit Payroll { get; } = new(database);

    /// <summary>Kartoteka z PESEL (gdy podany), adresem „ul. Lipowa 1/2” i urzędem skarbowym 1438.</summary>
    public async Task<Guid> PersonAsync(
        Guid company,
        string lastName,
        DateOnly birthDate,
        bool withPesel = true
    )
    {
        var employee = await Payroll.EmployeeAsync(
            company,
            lastName,
            birthDate,
            withPesel ? FakeIds.Pesel(birthDate) : null
        );
        // Kod z wykazu urzędów (słownik 002) — XSD sprawdza kod urzędu wyliczeniem.
        var office = await Payroll.AdminAsync<Guid>(
            "SELECT id FROM dictionaries.tax_offices WHERE code = '1438'"
        );
        await Payroll.AdminAsync<object>(
            """
            WITH a AS (
                INSERT INTO employees.addresses (employee_id, kind, street, postal_code, city, valid_from)
                VALUES ($1, 'residence', 'ul. Lipowa 1/2', '00-001', 'Przykładowo', DATE '2020-01-01')
            )
            UPDATE employees.employees SET tax_office_id = $2 WHERE id = $1
            """,
            employee,
            office
        );
        return employee;
    }

    /// <summary>Konto z rolą Pracownik powiązane z kartoteką; zwraca login.</summary>
    public async Task<string> LinkedUserAsync(Guid employee)
    {
        var login = Unique.Login();
        var user = await TestDb.CreateUserAsync(database, login, "Haslo-testowe-1", "pracownik");
        await Payroll.AdminAsync<object>(
            """
            INSERT INTO platform.user_employee_links (user_id, employee_id, company_id)
            SELECT $1, e.id, e.company_id FROM employees.employees e WHERE e.id = $2
            """,
            user,
            employee
        );
        return login;
    }

    public async Task<int> GenerateAsync(
        Guid company,
        Guid? employee = null,
        string login = "kadry"
    ) =>
        (int)
            (
                await Payroll.AsAsync<object>(
                    login,
                    "CALL documents_app.pit11_generate($1, 2026, $2, NULL)",
                    company,
                    employee
                )
            )!;

    /// <summary>Wiersze funkcji kontraktu jako konto (nazwa kolumny → wartość).</summary>
    public async Task<List<Dictionary<string, object?>>> RowsAsync(
        string login,
        string sql,
        params object?[] args
    )
    {
        await using var connection = await TestDb.ApiAsync(
            database,
            await TestDb.DevUserAsync(database, login)
        );
        await using var command = TestDb.Command(connection, sql);
        foreach (var arg in args)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = arg ?? DBNull.Value });
        }

        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            rows.Add(row);
        }

        return rows;
    }

    /// <summary>Ostatnia wersja PIT-11 kartoteki za 2026 (jako Kadry): wiersz tax_form_get.</summary>
    public async Task<Dictionary<string, object?>> FormAsync(Guid employee)
    {
        var id = await Payroll.AdminAsync<Guid>(
            "SELECT id FROM documents.tax_forms WHERE employee_id = $1 AND year = 2026 ORDER BY version DESC LIMIT 1",
            employee
        );
        return Assert.Single(
            await RowsAsync("kadry", "SELECT * FROM documents_app.tax_form_get($1)", id)
        );
    }

    public static JsonElement Data(Dictionary<string, object?> form) =>
        JsonElement.Parse((string)form["data"]!);

    /// <summary>Pozycje formularza P_xx → kwota.</summary>
    public static Dictionary<string, decimal> Positions(Dictionary<string, object?> form) =>
        Data(form)
            .GetProperty("positions")
            .EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetDecimal());
}
