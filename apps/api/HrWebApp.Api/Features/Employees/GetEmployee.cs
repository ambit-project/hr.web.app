using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HrWebApp.Api.Features.Employees;

public static class GetEmployee
{
    /// <summary>
    /// Pełne dane z PESEL i rachunkiem; każde wywołanie zapisuje audyt odczytu w bazie. Status z umów jak na liście;
    /// RetentionUntil — do kiedy przechowywać dokumentację (brak przy braku umów albo przy umowie bez końca,
    /// wtedy RetentionOpen = true).
    /// </summary>
    public sealed record Response(
        Guid Id,
        Guid CompanyId,
        string CompanyShortName,
        string Number,
        string FirstName,
        string? SecondName,
        string LastName,
        string? Pesel,
        string? IdentityDocument,
        DateOnly BirthDate,
        Guid? TaxOfficeId,
        string? TaxOfficeCode,
        string? TaxOfficeName,
        string? Email,
        string? Iban,
        string? AccountLogin,
        string Status,
        DateOnly? RetentionUntil,
        bool RetentionOpen,
        long RowVersion
    );

    public static async Task<Ok<Response>> Handle(
        Guid id,
        GetEmployeeHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.HandleAsync(id, cancellationToken));
}

public sealed class GetEmployeeHandler(DbSession db)
{
    public async Task<GetEmployee.Response> HandleAsync(
        Guid id,
        CancellationToken cancellationToken
    )
    {
        // Brak kartoteki zgłasza rutyna (U0002 → 404), więc wynik ma zawsze jeden wiersz.
        var rows = await db.QueryAsync(
            """
            SELECT id, company_id, company_short_name, number, first_name, second_name, last_name, pesel,
                   identity_document, birth_date, tax_office_id, tax_office_code, tax_office_name, email, iban,
                   account_login, status, retention_until, retention_open, row_version
            FROM employees_app.employee_get($1)
            """,
            [Db.Param(id)],
            reader => new GetEmployee.Response(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.GetFieldValue<DateOnly>(9),
                reader.IsDBNull(10) ? null : reader.GetGuid(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.IsDBNull(14) ? null : reader.GetString(14),
                reader.IsDBNull(15) ? null : reader.GetString(15),
                reader.GetString(16),
                reader.IsDBNull(17) ? null : reader.GetFieldValue<DateOnly>(17),
                reader.GetBoolean(18),
                reader.GetInt64(19)
            ),
            cancellationToken
        );
        return rows[0];
    }
}
