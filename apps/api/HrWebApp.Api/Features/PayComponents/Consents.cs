using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HrWebApp.Api.Features.PayComponents;

public static class ListConsents
{
    /// <summary>
    /// Status: <c>unsigned</c> (dokument niepodpisany — zgoda nie działa), <c>future</c>, <c>active</c>,
    /// <c>ended</c>. Pracownik czyta tylko kartotekę powiązaną ze swoim kontem.
    /// </summary>
    public sealed record Item(
        Guid Id,
        Guid ComponentTypeId,
        string Code,
        string Name,
        decimal MonthlyAmount,
        DateOnly ValidFrom,
        DateOnly? ValidTo,
        DateOnly? SignedAt,
        string Status,
        bool IsUsed,
        long RowVersion
    );

    public static async Task<Ok<List<Item>>> Handle(
        Guid id,
        ConsentsHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.ListAsync(id, cancellationToken));
}

public static class CreateConsent
{
    /// <summary>Pusta data podpisania = dokument przygotowany; zgoda działa od podpisania.</summary>
    public sealed record Request(
        Guid? ComponentTypeId,
        decimal? MonthlyAmount,
        DateOnly? ValidFrom,
        DateOnly? ValidTo,
        DateOnly? SignedAt
    );

    public sealed record Response(Guid Id);

    public static async Task<Ok<Response>> Handle(
        Guid id,
        Request request,
        ConsentsHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(new Response(await handler.CreateAsync(id, request, cancellationToken)));
}

public static class UpdateConsent
{
    /// <summary>Tylko niepodpisana (podpisana → 409 <c>signed</c>); ustawienie <c>SignedAt</c> to podpisanie.</summary>
    public sealed record Request(
        long RowVersion,
        decimal? MonthlyAmount,
        DateOnly? ValidFrom,
        DateOnly? ValidTo,
        DateOnly? SignedAt
    );

    public static async Task<NoContent> Handle(
        Guid id,
        Request request,
        ConsentsHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.UpdateAsync(id, request, cancellationToken);
        return TypedResults.NoContent();
    }
}

public static class EndConsent
{
    /// <summary>Zakończenie podpisanej zgody; pusta data cofa. Przed końcem składnika → 409 <c>consentInUse</c>.</summary>
    public sealed record Request(long RowVersion, DateOnly? ValidTo);

    public static async Task<NoContent> Handle(
        Guid id,
        Request request,
        ConsentsHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.EndAsync(id, request, cancellationToken);
        return TypedResults.NoContent();
    }
}

public static class DeleteConsent
{
    public static async Task<NoContent> Handle(
        Guid id,
        long rowVersion,
        ConsentsHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.DeleteAsync(id, rowVersion, cancellationToken);
        return TypedResults.NoContent();
    }
}

public sealed class ConsentsHandler(DbSession db)
{
    public async Task<List<ListConsents.Item>> ListAsync(
        Guid employeeId,
        CancellationToken cancellationToken
    ) =>
        await db.QueryAsync(
            """
            SELECT id, component_type_id, code, name, monthly_amount, valid_from, valid_to, signed_at, status,
                   is_used, row_version
            FROM pay_components_app.consent_list($1)
            """,
            [Db.Param(employeeId)],
            reader => new ListConsents.Item(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetDecimal(4),
                reader.GetFieldValue<DateOnly>(5),
                reader.IsDBNull(6) ? null : reader.GetFieldValue<DateOnly>(6),
                reader.IsDBNull(7) ? null : reader.GetFieldValue<DateOnly>(7),
                reader.GetString(8),
                reader.GetBoolean(9),
                reader.GetInt64(10)
            ),
            cancellationToken
        );

    public async Task<Guid> CreateAsync(
        Guid employeeId,
        CreateConsent.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<Guid>(
            "CALL pay_components_app.consent_create($1, $2, $3, $4, $5, $6, NULL)",
            [
                Db.Param(employeeId),
                Db.Param(request.ComponentTypeId),
                Db.Param(request.MonthlyAmount),
                Db.Param(request.ValidFrom),
                Db.Param(request.ValidTo),
                Db.Param(request.SignedAt),
            ],
            cancellationToken
        );

    public async Task UpdateAsync(
        Guid id,
        UpdateConsent.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<object>(
            "CALL pay_components_app.consent_update($1, $2, $3, $4, $5, $6)",
            [
                Db.Param(id),
                Db.Param(request.RowVersion),
                Db.Param(request.MonthlyAmount),
                Db.Param(request.ValidFrom),
                Db.Param(request.ValidTo),
                Db.Param(request.SignedAt),
            ],
            cancellationToken
        );

    public async Task EndAsync(
        Guid id,
        EndConsent.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<object>(
            "CALL pay_components_app.consent_end($1, $2, $3)",
            [Db.Param(id), Db.Param(request.RowVersion), Db.Param(request.ValidTo)],
            cancellationToken
        );

    public async Task DeleteAsync(Guid id, long rowVersion, CancellationToken cancellationToken) =>
        await db.ScalarAsync<object>(
            "CALL pay_components_app.consent_delete($1, $2)",
            [Db.Param(id), Db.Param(rowVersion)],
            cancellationToken
        );
}
