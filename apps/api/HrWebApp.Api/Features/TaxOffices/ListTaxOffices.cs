using HrWebApp.Api.Common;
using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace HrWebApp.Api.Features.TaxOffices;

public static class ListTaxOffices
{
    public sealed record Query(
        [FromQuery(Name = "search")] string? Search,
        [FromQuery(Name = "includeInactive")] bool? IncludeInactive,
        [FromQuery(Name = "page")] int? Page,
        [FromQuery(Name = "pageSize")] int? PageSize
    );

    public sealed record Item(Guid Id, string Code, string Name, bool IsActive, long RowVersion);

    public static async Task<Ok<PagedList<Item>>> Handle(
        [AsParameters] Query query,
        ListTaxOfficesHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.HandleAsync(query, cancellationToken));
}

public sealed class ListTaxOfficesHandler(DbSession db)
{
    public async Task<PagedList<ListTaxOffices.Item>> HandleAsync(
        ListTaxOffices.Query query,
        CancellationToken cancellationToken
    )
    {
        var page = Paging.Number(query.Page);
        var size = Paging.Size(query.PageSize);
        long total = 0;
        var items = await db.QueryAsync(
            """
            SELECT id, code, name, is_active, row_version, total_count
            FROM dictionaries_app.tax_office_list($1, $2, $3, $4)
            """,
            [
                Db.Param(query.Search),
                Db.Param(query.IncludeInactive ?? false),
                Db.Param(size),
                Db.Param(Paging.Offset(page, size)),
            ],
            reader =>
            {
                total = reader.GetInt64(5);
                return new ListTaxOffices.Item(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetBoolean(3),
                    reader.GetInt64(4)
                );
            },
            cancellationToken
        );
        return new PagedList<ListTaxOffices.Item>(items, total, page, size);
    }
}
