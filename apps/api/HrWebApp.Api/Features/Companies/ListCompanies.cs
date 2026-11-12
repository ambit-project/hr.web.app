using HrWebApp.Api.Common;
using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace HrWebApp.Api.Features.Companies;

public static class ListCompanies
{
    public sealed record Query(
        [FromQuery(Name = "search")] string? Search,
        [FromQuery(Name = "includeInactive")] bool? IncludeInactive,
        [FromQuery(Name = "page")] int? Page,
        [FromQuery(Name = "pageSize")] int? PageSize
    );

    public sealed record Item(
        Guid Id,
        string ShortName,
        string Name,
        string Nip,
        string Regon,
        string Street,
        string PostalCode,
        string City,
        bool IsActive,
        long RowVersion
    );

    public static async Task<Ok<PagedList<Item>>> Handle(
        [AsParameters] Query query,
        ListCompaniesHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.HandleAsync(query, cancellationToken));
}

public sealed class ListCompaniesHandler(DbSession db)
{
    public async Task<PagedList<ListCompanies.Item>> HandleAsync(
        ListCompanies.Query query,
        CancellationToken cancellationToken
    )
    {
        var page = Paging.Number(query.Page);
        var size = Paging.Size(query.PageSize);
        long total = 0;
        var items = await db.QueryAsync(
            """
            SELECT id, short_name, name, nip, regon, street, postal_code, city, is_active, row_version, total_count
            FROM platform_app.company_list($1, $2, $3, $4)
            """,
            [
                Db.Param(query.Search),
                Db.Param(query.IncludeInactive ?? false),
                Db.Param(size),
                Db.Param(Paging.Offset(page, size)),
            ],
            reader =>
            {
                total = reader.GetInt64(10);
                return new ListCompanies.Item(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.GetBoolean(8),
                    reader.GetInt64(9)
                );
            },
            cancellationToken
        );
        return new PagedList<ListCompanies.Item>(items, total, page, size);
    }
}
