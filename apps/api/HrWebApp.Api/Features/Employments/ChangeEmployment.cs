using System.ComponentModel.DataAnnotations;
using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HrWebApp.Api.Features.Employments;

public static class UpdateEmployment
{
    /// <summary>Rodzaju umowy i kartoteki nie zmienia się; początek pierwszej wersji warunków idzie za StartDate.</summary>
    public sealed record Request(
        long RowVersion,
        [property: MaxLength(40)] string Number,
        string? ContractType,
        DateOnly? SignedAt,
        DateOnly? StartDate,
        DateOnly? EndDate
    );

    public static async Task<NoContent> Handle(
        Guid id,
        Request request,
        EmploymentChangesHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.UpdateAsync(id, request, cancellationToken);
        return TypedResults.NoContent();
    }
}

public static class TerminateEmployment
{
    /// <summary>Pusta data cofa rozwiązanie.</summary>
    public sealed record Request(long RowVersion, DateOnly? TerminatedAt);

    public static async Task<NoContent> Handle(
        Guid id,
        Request request,
        EmploymentChangesHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.TerminateAsync(id, request, cancellationToken);
        return TypedResults.NoContent();
    }
}

public static class DeleteEmployment
{
    public static async Task<NoContent> Handle(
        Guid id,
        EmploymentChangesHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.DeleteAsync(id, cancellationToken);
        return TypedResults.NoContent();
    }
}

public static class AddEmploymentTerms
{
    /// <summary>
    /// Nowa wersja od <c>ValidFrom</c>: poprzednia kończy się dzień wcześniej; data początku ostatniej wersji
    /// poprawia ostatnią wersję.
    /// </summary>
    public sealed record Request(
        DateOnly? ValidFrom,
        Guid? PositionId,
        int? FteNumerator,
        int? FteDenominator,
        string? PayBasis,
        decimal? BaseAmount,
        string? ZusTitleCode,
        string? ZusPensionCode,
        string? ZusDisabilityCode,
        string[]? Insurances,
        string? CostKind
    ) : IEmploymentTermsFields;

    public sealed record Response(Guid Id);

    public static async Task<Ok<Response>> Handle(
        Guid id,
        Request request,
        EmploymentChangesHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(new Response(await handler.AddTermsAsync(id, request, cancellationToken)));
}

public static class DeleteLastEmploymentTerms
{
    /// <summary>Jedynej wersji nie usuwa się (409, <c>reason</c> = <c>onlyVersion</c>).</summary>
    public static async Task<NoContent> Handle(
        Guid id,
        EmploymentChangesHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.DeleteLastTermsAsync(id, cancellationToken);
        return TypedResults.NoContent();
    }
}

public sealed class EmploymentChangesHandler(DbSession db)
{
    public async Task UpdateAsync(
        Guid id,
        UpdateEmployment.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<object>(
            "CALL employment_app.employment_update($1, $2, $3, $4, $5, $6, $7)",
            [
                Db.Param(id),
                Db.Param(request.RowVersion),
                Db.Param(request.Number),
                Db.Param(request.ContractType),
                Db.Param(request.SignedAt),
                Db.Param(request.StartDate),
                Db.Param(request.EndDate),
            ],
            cancellationToken
        );

    public async Task TerminateAsync(
        Guid id,
        TerminateEmployment.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<object>(
            "CALL employment_app.employment_terminate($1, $2, $3)",
            [Db.Param(id), Db.Param(request.RowVersion), Db.Param(request.TerminatedAt)],
            cancellationToken
        );

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        await db.ScalarAsync<object>(
            "CALL employment_app.employment_delete($1)",
            [Db.Param(id)],
            cancellationToken
        );

    public async Task<Guid> AddTermsAsync(
        Guid id,
        AddEmploymentTerms.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<Guid>(
            "CALL employment_app.terms_add($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, NULL)",
            [Db.Param(id), Db.Param(request.ValidFrom), .. EmploymentTermsFields.Params(request)],
            cancellationToken
        );

    public async Task DeleteLastTermsAsync(Guid id, CancellationToken cancellationToken) =>
        await db.ScalarAsync<object>(
            "CALL employment_app.terms_delete_last($1)",
            [Db.Param(id)],
            cancellationToken
        );
}
