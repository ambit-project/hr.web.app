using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HrWebApp.Api.Features.OptimaExport;

public static class ExportEmployeesToOptima
{
    /// <summary>
    /// Kartoteki spółki jako plik <c>.xls</c> do importu w Comarch ERP Optima (Narzędzia / Importy / Danych kadrowych
    /// / z arkusza MS Excel): wskazane <c>EmployeeIds</c> albo — gdy lista pusta — wszystkie pasujące do filtra listy
    /// (<c>Search</c>, <c>Status</c>). <c>CompanyId</c> wymagane (<c>required</c>, <c>notFound</c>). Wypełnione tylko
    /// kolumny o potwierdzonym formacie: akronim (<c>Kod</c>), imiona, nazwisko, data urodzenia, płeć z PESEL, PESEL,
    /// archiwalny, adres zamieszkania i korespondencyjny, e-mail. Odczyt PESEL w audycie odczytów.
    /// </summary>
    public sealed record Request(
        Guid? CompanyId,
        Guid[]? EmployeeIds,
        string? Search,
        string? Status
    );

    public static async Task<Results<FileContentHttpResult, ValidationProblem>> Handle(
        Request request,
        ExportEmployeesToOptimaHandler handler,
        HttpContext http,
        CancellationToken cancellationToken
    )
    {
        var rows = await handler.RowsAsync(request, cancellationToken);
        if (rows.Count > OptimaHrImportSheet.MaxRows)
        {
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["employeeIds"] = ["tooMany"] }
            );
        }
        // PESEL i adresy: bez kopii w pamięci podręcznej przeglądarki ani proxy.
        http.Response.Headers.CacheControl = "no-store";
        return TypedResults.File(
            OptimaHrImportSheet.Write(rows.Select(OptimaEmployeeRow.From)),
            OptimaHrImportSheet.ContentType,
            $"optima-kadry-{DateTime.UtcNow:yyyy-MM-dd}.xls"
        );
    }
}

/// <summary>Kartoteka z <c>documents_app.optima_export</c>.</summary>
public sealed record OptimaEmployee(
    string Acronym,
    string FirstName,
    string? SecondName,
    string LastName,
    DateOnly BirthDate,
    string? Pesel,
    bool Archived,
    string? Email,
    OptimaAddress? Residence,
    OptimaAddress? Correspondence
);

public sealed record OptimaAddress(
    string Country,
    string City,
    string Street,
    string? House,
    string? Apartment,
    string PostalCode
);

public sealed class ExportEmployeesToOptimaHandler(DbSession db)
{
    public Task<List<OptimaEmployee>> RowsAsync(
        ExportEmployeesToOptima.Request request,
        CancellationToken cancellationToken
    ) =>
        db.QueryAsync(
            """
            SELECT acronym, first_name, second_name, last_name, birth_date, pesel, archived, email,
                   residence_country, residence_city, residence_street, residence_house, residence_apartment,
                   residence_postal_code,
                   correspondence_country, correspondence_city, correspondence_street, correspondence_house,
                   correspondence_apartment, correspondence_postal_code
            FROM documents_app.optima_export($1, $2, $3, $4)
            """,
            [
                Db.Param(request.CompanyId),
                Db.Param(request.EmployeeIds ?? []),
                Db.Param(request.Search),
                Db.Param(request.Status),
            ],
            reader => new OptimaEmployee(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.GetFieldValue<DateOnly>(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetBoolean(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                Address(reader, 8),
                Address(reader, 14)
            ),
            cancellationToken
        );

    private static OptimaAddress? Address(Npgsql.NpgsqlDataReader reader, int first) =>
        reader.IsDBNull(first)
            ? null
            : new OptimaAddress(
                reader.GetString(first),
                reader.GetString(first + 1),
                reader.GetString(first + 2),
                reader.IsDBNull(first + 3) ? null : reader.GetString(first + 3),
                reader.IsDBNull(first + 4) ? null : reader.GetString(first + 4),
                reader.GetString(first + 5)
            );
}

/// <summary>Kartoteka jako wiersz arkusza Optimy (<see cref="OptimaHrImportSheet"/>).</summary>
public static class OptimaEmployeeRow
{
    public static IReadOnlyDictionary<string, object?> From(OptimaEmployee employee)
    {
        var row = new Dictionary<string, object?>
        {
            ["Kod"] = employee.Acronym,
            ["Nazwisko"] = employee.LastName,
            ["Imie"] = employee.FirstName,
            ["Archiwalny"] = employee.Archived,
            ["Drugie_imie"] = employee.SecondName,
            ["Data_urodzenia"] = employee.BirthDate,
            ["Plec"] = Sex(employee.Pesel),
            ["Pesel"] = employee.Pesel,
            ["Email"] = employee.Email,
        };
        // Zameldowania (blok bez przyrostka) kartoteka nie zna — zostaje domyślny Optimy.
        AddAddress(row, "_zamieszkania", employee.Residence);
        AddAddress(row, "_korespondencja", employee.Correspondence);
        return row;
    }

    /// <summary>K albo M — 10. cyfra PESEL (parzysta — kobieta); bez PESEL puste.</summary>
    public static string? Sex(string? pesel) =>
        pesel is { Length: 11 } && pesel.All(char.IsAsciiDigit)
            ? (pesel[9] - '0') % 2 == 0
                ? "K"
                : "M"
            : null;

    private static void AddAddress(
        Dictionary<string, object?> row,
        string suffix,
        OptimaAddress? address
    )
    {
        if (address is null)
        {
            return;
        }
        row["Kraj" + suffix] = OptimaCountries.Name(address.Country);
        row["Kod_kraju" + suffix] = address.Country.ToUpperInvariant();
        row["Miejscowosc" + suffix] = address.City;
        row["Ulica" + suffix] = address.Street;
        row["Nr_domu" + suffix] = address.House;
        row["Nr_lokalu" + suffix] = address.Apartment;
        row["Kod_pocztowy" + suffix] = address.PostalCode;
    }
}
