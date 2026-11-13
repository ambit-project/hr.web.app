using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HrWebApp.Api.Features.TaxDeclarations;

public static class ListTaxDeclarations
{
    /// <summary>
    /// Kind: <c>pit2</c>, <c>youthReliefWaiver</c>, <c>returnRelief</c>, <c>familyRelief</c>,
    /// <c>seniorRelief</c>, <c>increasedCosts</c>, <c>employeeCostsWaiver</c>, <c>authorCostsWaiver</c>,
    /// <c>noAdvances</c>, <c>higherRate</c>. Share (PIT-2): <c>1/12</c>, <c>1/24</c>, <c>1/36</c>.
    /// Status: <c>future</c>, <c>active</c>, <c>ended</c>, <c>lapsed</c> (po końcu umowy). Pracownik czyta tylko
    /// kartotekę powiązaną ze swoim kontem.
    /// </summary>
    public sealed record Item(
        Guid Id,
        string Kind,
        string? Share,
        int? YearFrom,
        int? YearTo,
        DateOnly SubmittedAt,
        DateOnly ValidFrom,
        DateOnly? ValidTo,
        DateOnly? WithdrawnAt,
        DateOnly? LapsesOn,
        string Status,
        long RowVersion
    );

    public static async Task<Ok<List<Item>>> Handle(
        Guid id,
        bool? onlyActive,
        TaxDeclarationsHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.ListAsync(id, onlyActive ?? false, cancellationToken));
}

public static class CreateTaxDeclaration
{
    /// <summary>
    /// <c>ValidFrom</c> to pierwszy dzień miesiąca złożenia albo następnego; <c>ValidTo</c> (ostatni dzień
    /// miesiąca) opcjonalny — <c>noAdvances</c> i <c>returnRelief</c> dostają koniec roku.
    /// </summary>
    public sealed record Request(
        string Kind,
        string? Share,
        int? YearFrom,
        int? YearTo,
        DateOnly? SubmittedAt,
        DateOnly? ValidFrom,
        DateOnly? ValidTo
    );

    public sealed record Response(Guid Id);

    public static async Task<Created<Response>> Handle(
        Guid id,
        Request request,
        TaxDeclarationsHandler handler,
        CancellationToken cancellationToken
    )
    {
        var declarationId = await handler.CreateAsync(id, request, cancellationToken);
        return TypedResults.Created(
            $"/api/tax-declarations/{declarationId}",
            new Response(declarationId)
        );
    }
}

public static class EndTaxDeclaration
{
    /// <summary>
    /// Wycofanie złożone <c>WithdrawnAt</c>, wpis nie obowiązuje od miesiąca <c>StopFrom</c>. Obie daty puste
    /// cofają zakończenie.
    /// </summary>
    public sealed record Request(long RowVersion, DateOnly? WithdrawnAt, DateOnly? StopFrom);

    public static async Task<NoContent> Handle(
        Guid id,
        Request request,
        TaxDeclarationsHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.EndAsync(id, request, cancellationToken);
        return TypedResults.NoContent();
    }
}

public static class DeleteTaxDeclaration
{
    public static async Task<NoContent> Handle(
        Guid id,
        long rowVersion,
        TaxDeclarationsHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.DeleteAsync(id, rowVersion, cancellationToken);
        return TypedResults.NoContent();
    }
}

public sealed class TaxDeclarationsHandler(DbSession db)
{
    public async Task<List<ListTaxDeclarations.Item>> ListAsync(
        Guid employeeId,
        bool onlyActive,
        CancellationToken cancellationToken
    ) =>
        await db.QueryAsync(
            """
            SELECT id, kind, share, year_from, year_to, submitted_at, valid_from, valid_to, withdrawn_at,
                   lapses_on, status, row_version
            FROM tax_declarations_app.declaration_list($1, $2)
            """,
            [Db.Param(employeeId), Db.Param(onlyActive)],
            reader => new ListTaxDeclarations.Item(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.GetFieldValue<DateOnly>(5),
                reader.GetFieldValue<DateOnly>(6),
                reader.IsDBNull(7) ? null : reader.GetFieldValue<DateOnly>(7),
                reader.IsDBNull(8) ? null : reader.GetFieldValue<DateOnly>(8),
                reader.IsDBNull(9) ? null : reader.GetFieldValue<DateOnly>(9),
                reader.GetString(10),
                reader.GetInt64(11)
            ),
            cancellationToken
        );

    public async Task<Guid> CreateAsync(
        Guid employeeId,
        CreateTaxDeclaration.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<Guid>(
            "CALL tax_declarations_app.declaration_create($1, $2, $3, $4, $5, $6, $7, $8, NULL)",
            [
                Db.Param(employeeId),
                Db.Param(request.Kind),
                Db.Param(request.Share),
                Db.Param(request.YearFrom),
                Db.Param(request.YearTo),
                Db.Param(request.SubmittedAt),
                Db.Param(request.ValidFrom),
                Db.Param(request.ValidTo),
            ],
            cancellationToken
        );

    public async Task EndAsync(
        Guid id,
        EndTaxDeclaration.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<object>(
            "CALL tax_declarations_app.declaration_end($1, $2, $3, $4)",
            [
                Db.Param(id),
                Db.Param(request.RowVersion),
                Db.Param(request.WithdrawnAt),
                Db.Param(request.StopFrom),
            ],
            cancellationToken
        );

    public async Task DeleteAsync(Guid id, long rowVersion, CancellationToken cancellationToken) =>
        await db.ScalarAsync<object>(
            "CALL tax_declarations_app.declaration_delete($1, $2)",
            [Db.Param(id), Db.Param(rowVersion)],
            cancellationToken
        );
}
