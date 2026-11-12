using HrWebApp.Api.Common;
using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace HrWebApp.Api.Features.ZusCodes;

public static class ListZusCodes
{
    /// <summary>Kind: <c>title</c>, <c>pension</c> albo <c>disability</c>; inne wartości dają pustą listę.</summary>
    public sealed record Query(
        [FromQuery(Name = "kind")] string? Kind,
        [FromQuery(Name = "search")] string? Search,
        [FromQuery(Name = "onDate")] DateOnly? OnDate,
        [FromQuery(Name = "page")] int? Page,
        [FromQuery(Name = "pageSize")] int? PageSize
    );

    public sealed record Item(
        Guid Id,
        string Kind,
        string Code,
        string Name,
        DateOnly ValidFrom,
        DateOnly? ValidTo,
        long RowVersion
    );

    public static async Task<Ok<PagedList<Item>>> Handle(
        [AsParameters] Query query,
        ListZusCodesHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.HandleAsync(query, cancellationToken));
}

public sealed class ListZusCodesHandler(DbSession db)
{
    public async Task<PagedList<ListZusCodes.Item>> HandleAsync(
        ListZusCodes.Query query,
        CancellationToken cancellationToken
    )
    {
        var page = Paging.Number(query.Page);
        var size = Paging.Size(query.PageSize);
        long total = 0;
        var items = await db.QueryAsync(
            """
            SELECT id, kind, code, name, valid_from, valid_to, row_version, total_count
            FROM dictionaries_app.zus_code_list($1, $2, $3, $4, $5)
            """,
            [
                Db.Param(query.Kind),
                Db.Param(query.Search),
                Db.Param(query.OnDate),
                Db.Param(size),
                Db.Param(Paging.Offset(page, size)),
            ],
            reader =>
            {
                total = reader.GetInt64(7);
                return new ListZusCodes.Item(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetFieldValue<DateOnly>(4),
                    reader.IsDBNull(5) ? null : reader.GetFieldValue<DateOnly>(5),
                    reader.GetInt64(6)
                );
            },
            cancellationToken
        );
        return new PagedList<ListZusCodes.Item>(items, total, page, size);
    }
}
