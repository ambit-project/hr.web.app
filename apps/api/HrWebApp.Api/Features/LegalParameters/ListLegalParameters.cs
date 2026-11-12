using HrWebApp.Api.Common;
using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace HrWebApp.Api.Features.LegalParameters;

public static class ListLegalParameters
{
    public sealed record Query(
        [FromQuery(Name = "code")] string? Code,
        [FromQuery(Name = "onDate")] DateOnly? OnDate,
        [FromQuery(Name = "page")] int? Page,
        [FromQuery(Name = "pageSize")] int? PageSize
    );

    public sealed record Item(
        Guid Id,
        string Code,
        decimal Value,
        DateOnly ValidFrom,
        DateOnly? ValidTo,
        string Source
    );

    public static async Task<Ok<PagedList<Item>>> Handle(
        [AsParameters] Query query,
        ListLegalParametersHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.HandleAsync(query, cancellationToken));
}

public sealed class ListLegalParametersHandler(DbSession db)
{
    public async Task<PagedList<ListLegalParameters.Item>> HandleAsync(
        ListLegalParameters.Query query,
        CancellationToken cancellationToken
    )
    {
        var page = Paging.Number(query.Page);
        var size = Paging.Size(query.PageSize);
        long total = 0;
        var items = await db.QueryAsync(
            """
            SELECT id, code, value, valid_from, valid_to, source, total_count
            FROM platform_app.legal_parameter_list($1, $2, $3, $4)
            """,
            [
                Db.Param(query.Code),
                Db.Param(query.OnDate),
                Db.Param(size),
                Db.Param(Paging.Offset(page, size)),
            ],
            reader =>
            {
                total = reader.GetInt64(6);
                return new ListLegalParameters.Item(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetDecimal(2),
                    reader.GetFieldValue<DateOnly>(3),
                    reader.IsDBNull(4) ? null : reader.GetFieldValue<DateOnly>(4),
                    reader.GetString(5)
                );
            },
            cancellationToken
        );
        return new PagedList<ListLegalParameters.Item>(items, total, page, size);
    }
}
