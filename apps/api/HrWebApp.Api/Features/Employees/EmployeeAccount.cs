using HrWebApp.Api.Common;
using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace HrWebApp.Api.Features.Employees;

public static class LinkEmployeeAccount
{
    /// <summary>Login albo e-mail konta z rolą Pracownik.</summary>
    public sealed record Request(string Account);

    public static async Task<NoContent> Handle(
        Guid id,
        Request request,
        EmployeeAccountHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.LinkAsync(id, request.Account, cancellationToken);
        return TypedResults.NoContent();
    }
}

public static class UnlinkEmployeeAccount
{
    public static async Task<NoContent> Handle(
        Guid id,
        EmployeeAccountHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.UnlinkAsync(id, cancellationToken);
        return TypedResults.NoContent();
    }
}

public static class ListEmployeeReadAudit
{
    public sealed record Query(
        [FromQuery(Name = "page")] int? Page,
        [FromQuery(Name = "pageSize")] int? PageSize
    );

    public sealed record Item(long Id, DateTimeOffset At, string ActorName, string Resource);

    public static async Task<Ok<PagedList<Item>>> Handle(
        Guid id,
        [AsParameters] Query query,
        EmployeeAccountHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.ReadAuditAsync(id, query, cancellationToken));
}

public sealed class EmployeeAccountHandler(DbSession db)
{
    public async Task LinkAsync(
        Guid employeeId,
        string account,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<object>(
            "CALL employees_app.account_link($1, $2)",
            [Db.Param(employeeId), Db.Param(account)],
            cancellationToken
        );

    public async Task UnlinkAsync(Guid employeeId, CancellationToken cancellationToken) =>
        await db.ScalarAsync<object>(
            "CALL employees_app.account_unlink($1)",
            [Db.Param(employeeId)],
            cancellationToken
        );

    public async Task<PagedList<ListEmployeeReadAudit.Item>> ReadAuditAsync(
        Guid employeeId,
        ListEmployeeReadAudit.Query query,
        CancellationToken cancellationToken
    )
    {
        var page = Paging.Number(query.Page);
        var size = Paging.Size(query.PageSize);
        long total = 0;
        var items = await db.QueryAsync(
            "SELECT id, at, actor_name, resource, total_count FROM employees_app.read_audit_list($1, $2, $3)",
            [Db.Param(employeeId), Db.Param(size), Db.Param(Paging.Offset(page, size))],
            reader =>
            {
                total = reader.GetInt64(4);
                return new ListEmployeeReadAudit.Item(
                    reader.GetInt64(0),
                    reader.GetFieldValue<DateTimeOffset>(1),
                    reader.GetString(2),
                    reader.GetString(3)
                );
            },
            cancellationToken
        );
        return new PagedList<ListEmployeeReadAudit.Item>(items, total, page, size);
    }
}
