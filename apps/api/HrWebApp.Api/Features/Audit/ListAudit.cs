using HrWebApp.Api.Common;
using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace HrWebApp.Api.Features.Audit;

public static class ListAudit
{
    public sealed record Query(
        [FromQuery(Name = "entity")] string? Entity,
        [FromQuery(Name = "entityId")] string? EntityId,
        [FromQuery(Name = "actorId")] Guid? ActorId,
        [FromQuery(Name = "from")] DateOnly? From,
        [FromQuery(Name = "to")] DateOnly? To,
        [FromQuery(Name = "page")] int? Page,
        [FromQuery(Name = "pageSize")] int? PageSize
    );

    /// <summary>Before i After: stan encji jako tekst JSON (pola w nazwach z bazy), bo kształt zależy od encji.</summary>
    public sealed record Item(
        long Id,
        DateTimeOffset At,
        Guid? ActorId,
        string? ActorName,
        string Entity,
        string EntityId,
        string Action,
        string? Before,
        string? After
    );

    public static IEndpointRouteBuilder MapAudit(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/audit").WithTags("Audit").MapGet("/", Handle).WithName("ListAudit");
        return app;
    }

    public static async Task<Ok<PagedList<Item>>> Handle(
        [AsParameters] Query query,
        ListAuditHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.HandleAsync(query, cancellationToken));
}

public sealed class ListAuditHandler(DbSession db)
{
    public async Task<PagedList<ListAudit.Item>> HandleAsync(
        ListAudit.Query query,
        CancellationToken cancellationToken
    )
    {
        var page = Paging.Number(query.Page);
        var size = Paging.Size(query.PageSize);
        long total = 0;
        var items = await db.QueryAsync(
            """
            SELECT id, at, actor_id, actor_name, entity, entity_id, action, before::text, after::text, total_count
            FROM platform_app.audit_list($1, $2, $3, $4, $5, $6, $7)
            """,
            [
                Db.Param(query.Entity),
                Db.Param(query.EntityId),
                Db.Param(query.ActorId),
                Db.Param(query.From),
                Db.Param(query.To),
                Db.Param(size),
                Db.Param(Paging.Offset(page, size)),
            ],
            reader =>
            {
                total = reader.GetInt64(9);
                return new ListAudit.Item(
                    reader.GetInt64(0),
                    reader.GetFieldValue<DateTimeOffset>(1),
                    reader.IsDBNull(2) ? null : reader.GetGuid(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8)
                );
            },
            cancellationToken
        );
        return new PagedList<ListAudit.Item>(items, total, page, size);
    }
}
