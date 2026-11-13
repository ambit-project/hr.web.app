using System.ComponentModel.DataAnnotations;
using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HrWebApp.Api.Features.PayComponents;

public static class ListGarnishments
{
    /// <summary>
    /// Kind: <c>alimony</c>, <c>other</c>. AuthorityKind: <c>court</c>, <c>administrative</c>, <c>creditor</c>
    /// (wierzyciel alimentacyjny z tytułem wykonawczym). Status: <c>active</c>, <c>paidOff</c>,
    /// <c>discontinued</c>. CollectedAmount wyliczy lista płac (009). Odczyt trafia do audytu odczytów.
    /// </summary>
    public sealed record Item(
        Guid Id,
        string Kind,
        string AuthorityKind,
        string AuthorityName,
        string CaseNumber,
        DateOnly ReceivedAt,
        decimal? MonthlyAmount,
        decimal? TotalAmount,
        decimal CollectedAmount,
        string Status,
        DateOnly? StatusDate,
        string? Note,
        long RowVersion
    );

    public static async Task<Ok<List<Item>>> Handle(
        Guid id,
        GarnishmentsHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.ListAsync(id, cancellationToken));
}

public static class CreateGarnishment
{
    public sealed record Request(
        string? Kind,
        string? AuthorityKind,
        [property: MaxLength(300)] string? AuthorityName,
        [property: MaxLength(100)] string? CaseNumber,
        DateOnly? ReceivedAt,
        decimal? MonthlyAmount,
        decimal? TotalAmount,
        [property: MaxLength(2000)] string? Note
    );

    public sealed record Response(Guid Id);

    public static async Task<Ok<Response>> Handle(
        Guid id,
        Request request,
        GarnishmentsHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(new Response(await handler.CreateAsync(id, request, cancellationToken)));
}

public static class UpdateGarnishment
{
    public sealed record Request(
        long RowVersion,
        string? Kind,
        string? AuthorityKind,
        [property: MaxLength(300)] string? AuthorityName,
        [property: MaxLength(100)] string? CaseNumber,
        DateOnly? ReceivedAt,
        decimal? MonthlyAmount,
        decimal? TotalAmount,
        [property: MaxLength(2000)] string? Note
    );

    public static async Task<NoContent> Handle(
        Guid id,
        Request request,
        GarnishmentsHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.UpdateAsync(id, request, cancellationToken);
        return TypedResults.NoContent();
    }
}

public static class SetGarnishmentStatus
{
    /// <summary><c>active</c> bez daty; <c>paidOff</c> i <c>discontinued</c> z datą nie wcześniejszą niż doręczenie.</summary>
    public sealed record Request(long RowVersion, string? Status, DateOnly? StatusDate);

    public static async Task<NoContent> Handle(
        Guid id,
        Request request,
        GarnishmentsHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.SetStatusAsync(id, request, cancellationToken);
        return TypedResults.NoContent();
    }
}

public static class DeleteGarnishment
{
    public static async Task<NoContent> Handle(
        Guid id,
        long rowVersion,
        GarnishmentsHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.DeleteAsync(id, rowVersion, cancellationToken);
        return TypedResults.NoContent();
    }
}

public sealed class GarnishmentsHandler(DbSession db)
{
    public async Task<List<ListGarnishments.Item>> ListAsync(
        Guid employeeId,
        CancellationToken cancellationToken
    ) =>
        await db.QueryAsync(
            """
            SELECT id, kind, authority_kind, authority_name, case_number, received_at, monthly_amount,
                   total_amount, collected_amount, status, status_date, note, row_version
            FROM pay_components_app.garnishment_list($1)
            """,
            [Db.Param(employeeId)],
            reader => new ListGarnishments.Item(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetFieldValue<DateOnly>(5),
                reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                reader.GetDecimal(8),
                reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetFieldValue<DateOnly>(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.GetInt64(12)
            ),
            cancellationToken
        );

    public async Task<Guid> CreateAsync(
        Guid employeeId,
        CreateGarnishment.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<Guid>(
            "CALL pay_components_app.garnishment_create($1, $2, $3, $4, $5, $6, $7, $8, $9, NULL)",
            [
                Db.Param(employeeId),
                Db.Param(request.Kind),
                Db.Param(request.AuthorityKind),
                Db.Param(request.AuthorityName),
                Db.Param(request.CaseNumber),
                Db.Param(request.ReceivedAt),
                Db.Param(request.MonthlyAmount),
                Db.Param(request.TotalAmount),
                Db.Param(request.Note),
            ],
            cancellationToken
        );

    public async Task UpdateAsync(
        Guid id,
        UpdateGarnishment.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<object>(
            "CALL pay_components_app.garnishment_update($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)",
            [
                Db.Param(id),
                Db.Param(request.RowVersion),
                Db.Param(request.Kind),
                Db.Param(request.AuthorityKind),
                Db.Param(request.AuthorityName),
                Db.Param(request.CaseNumber),
                Db.Param(request.ReceivedAt),
                Db.Param(request.MonthlyAmount),
                Db.Param(request.TotalAmount),
                Db.Param(request.Note),
            ],
            cancellationToken
        );

    public async Task SetStatusAsync(
        Guid id,
        SetGarnishmentStatus.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<object>(
            "CALL pay_components_app.garnishment_set_status($1, $2, $3, $4)",
            [
                Db.Param(id),
                Db.Param(request.RowVersion),
                Db.Param(request.Status),
                Db.Param(request.StatusDate),
            ],
            cancellationToken
        );

    public async Task DeleteAsync(Guid id, long rowVersion, CancellationToken cancellationToken) =>
        await db.ScalarAsync<object>(
            "CALL pay_components_app.garnishment_delete($1, $2)",
            [Db.Param(id), Db.Param(rowVersion)],
            cancellationToken
        );
}
