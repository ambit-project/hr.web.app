using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HrWebApp.Api.Features.Employees;

public static class CopyEmployees
{
    /// <summary>
    /// Kopiuje kartoteki do spółki docelowej: wskazane <c>EmployeeIds</c> albo — gdy lista pusta — wszystkie
    /// pasujące do filtra listy (<c>CompanyId</c>, <c>Search</c>, <c>Status</c>). Najwyżej 500 naraz.
    /// Pominięte mają powód: <c>sameCompany</c>, <c>peselExists</c>, <c>personExists</c>.
    /// </summary>
    public sealed record Request(
        Guid? TargetCompanyId,
        Guid[]? EmployeeIds,
        Guid? CompanyId,
        string? Search,
        string? Status
    );

    public sealed record Skipped(Guid EmployeeId, string Name, string Reason);

    public sealed record Response(int Copied, List<Skipped> Skipped);

    public static async Task<Ok<Response>> Handle(
        Request request,
        CopyEmployeesHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.HandleAsync(request, cancellationToken));
}

public sealed class CopyEmployeesHandler(DbSession db)
{
    public async Task<CopyEmployees.Response> HandleAsync(
        CopyEmployees.Request request,
        CancellationToken cancellationToken
    )
    {
        var rows = await db.QueryAsync(
            """
            SELECT source_id, source_name, new_id, skipped_reason
            FROM employees_app.employee_copy($1, $2, $3, $4, $5)
            """,
            [
                Db.Param(request.TargetCompanyId),
                Db.Param(request.EmployeeIds ?? []),
                Db.Param(request.CompanyId),
                Db.Param(request.Search),
                Db.Param(request.Status),
            ],
            reader =>
                (
                    Id: reader.GetGuid(0),
                    Name: reader.GetString(1),
                    Reason: reader.IsDBNull(3) ? null : reader.GetString(3)
                ),
            cancellationToken
        );
        return new CopyEmployees.Response(
            rows.Count(row => row.Reason is null),
            [
                .. rows.Where(row => row.Reason is not null)
                    .Select(row => new CopyEmployees.Skipped(row.Id, row.Name, row.Reason!)),
            ]
        );
    }
}
