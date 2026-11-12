using HrWebApp.Api.Common;
using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace HrWebApp.Api.Features.Positions;

public static class ListPositions
{
    public sealed record Query(
        [FromQuery(Name = "search")] string? Search,
        [FromQuery(Name = "includeInactive")] bool? IncludeInactive,
        [FromQuery(Name = "page")] int? Page,
        [FromQuery(Name = "pageSize")] int? PageSize
    );

    public sealed record Item(
        Guid Id,
        string Name,
        string? Description,
        bool IsActive,
        DateTimeOffset CreatedAt
    );

    public static async Task<Ok<PagedList<Item>>> Handle(
        [AsParameters] Query query,
        ListPositionsHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.HandleAsync(query, cancellationToken));
}

public sealed class ListPositionsHandler(DbSession db)
{
    public async Task<PagedList<ListPositions.Item>> HandleAsync(
        ListPositions.Query query,
        CancellationToken cancellationToken
    )
    {
        var page = Paging.Number(query.Page);
        var size = Paging.Size(query.PageSize);
        long total = 0;

        var items = await db.QueryAsync(
            """
            SELECT id, name, description, is_active, created_at, total_count
            FROM dictionaries_app.position_list($1, $2, $3, $4)
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
                return new ListPositions.Item(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetBoolean(3),
                    reader.GetFieldValue<DateTimeOffset>(4)
                );
            },
            cancellationToken
        );

        return new PagedList<ListPositions.Item>(items, total, page, size);
    }
}
