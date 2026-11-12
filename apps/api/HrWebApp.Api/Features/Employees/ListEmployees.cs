using HrWebApp.Api.Common;
using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace HrWebApp.Api.Features.Employees;

public static class ListEmployees
{
    /// <summary>Search: nazwisko, imię, numer; pełny poprawny PESEL szuka dokładnie (HMAC w bazie).</summary>
    public sealed record Query(
        [FromQuery(Name = "companyId")] Guid? CompanyId,
        [FromQuery(Name = "search")] string? Search,
        [FromQuery(Name = "page")] int? Page,
        [FromQuery(Name = "pageSize")] int? PageSize
    );

    public sealed record Item(
        Guid Id,
        Guid CompanyId,
        string CompanyShortName,
        string Number,
        string FirstName,
        string? SecondName,
        string LastName,
        DateOnly BirthDate,
        string? City,
        string? AccountLogin
    );

    public static async Task<Ok<PagedList<Item>>> Handle(
        [AsParameters] Query query,
        ListEmployeesHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.HandleAsync(query, cancellationToken));
}

public sealed class ListEmployeesHandler(DbSession db)
{
    public async Task<PagedList<ListEmployees.Item>> HandleAsync(
        ListEmployees.Query query,
        CancellationToken cancellationToken
    )
    {
        var page = Paging.Number(query.Page);
        var size = Paging.Size(query.PageSize);
        long total = 0;
        var items = await db.QueryAsync(
            """
            SELECT id, company_id, company_short_name, number, first_name, second_name, last_name, birth_date,
                   city, account_login, total_count
            FROM employees_app.employee_list($1, $2, $3, $4)
            """,
            [
                Db.Param(query.CompanyId),
                Db.Param(query.Search),
                Db.Param(size),
                Db.Param(Paging.Offset(page, size)),
            ],
            reader =>
            {
                total = reader.GetInt64(10);
                return new ListEmployees.Item(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.GetString(6),
                    reader.GetFieldValue<DateOnly>(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9)
                );
            },
            cancellationToken
        );
        return new PagedList<ListEmployees.Item>(items, total, page, size);
    }
}
