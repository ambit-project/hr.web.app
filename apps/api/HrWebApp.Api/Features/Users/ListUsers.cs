using HrWebApp.Api.Common;
using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace HrWebApp.Api.Features.Users;

public static class ListUsers
{
    public sealed record Query(
        [FromQuery(Name = "search")] string? Search,
        [FromQuery(Name = "page")] int? Page,
        [FromQuery(Name = "pageSize")] int? PageSize
    );

    public sealed record Item(
        Guid Id,
        string Login,
        string Email,
        string DisplayName,
        IReadOnlyList<string> Roles,
        bool IsActive,
        bool IsDevAccount,
        bool IsLocked,
        long RowVersion
    );

    public static async Task<Ok<PagedList<Item>>> Handle(
        [AsParameters] Query query,
        ListUsersHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.HandleAsync(query, cancellationToken));
}

public sealed class ListUsersHandler(DbSession db)
{
    public async Task<PagedList<ListUsers.Item>> HandleAsync(
        ListUsers.Query query,
        CancellationToken cancellationToken
    )
    {
        var page = Paging.Number(query.Page);
        var size = Paging.Size(query.PageSize);
        long total = 0;
        var items = await db.QueryAsync(
            """
            SELECT id, login, email, display_name, roles, is_active, is_dev_account, is_locked, row_version, total_count
            FROM platform_app.user_list($1, $2, $3)
            """,
            [Db.Param(query.Search), Db.Param(size), Db.Param(Paging.Offset(page, size))],
            reader =>
            {
                total = reader.GetInt64(9);
                return new ListUsers.Item(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetFieldValue<string[]>(4),
                    reader.GetBoolean(5),
                    reader.GetBoolean(6),
                    reader.GetBoolean(7),
                    reader.GetInt64(8)
                );
            },
            cancellationToken
        );
        return new PagedList<ListUsers.Item>(items, total, page, size);
    }
}
