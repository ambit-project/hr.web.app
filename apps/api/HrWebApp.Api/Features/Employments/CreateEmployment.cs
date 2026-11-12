using System.ComponentModel.DataAnnotations;
using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HrWebApp.Api.Features.Employments;

public static class CreateEmployment
{
    /// <summary>
    /// Umowa z pierwszą wersją warunków od <c>StartDate</c>. Pusty numer = kolejny wolny numer umów w spółce.
    /// Zlecenie albo dzieło w okresie umowy o pracę tej samej kartoteki → 409 z <c>reason</c> =
    /// <c>concurrentTitleUnsupported</c>.
    /// </summary>
    public sealed record Request(
        [property: MaxLength(40)] string? Number,
        string Kind,
        string? ContractType,
        DateOnly? SignedAt,
        DateOnly? StartDate,
        DateOnly? EndDate,
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

    public static async Task<Created<Response>> Handle(
        Guid id,
        Request request,
        CreateEmploymentHandler handler,
        CancellationToken cancellationToken
    )
    {
        var employmentId = await handler.HandleAsync(id, request, cancellationToken);
        return TypedResults.Created($"/api/employments/{employmentId}", new Response(employmentId));
    }
}

public sealed class CreateEmploymentHandler(DbSession db)
{
    public async Task<Guid> HandleAsync(
        Guid employeeId,
        CreateEmployment.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<Guid>(
            """
            CALL employment_app.employment_create($1, $2, $3, $4, $5, $6, $7,
                $8, $9, $10, $11, $12, $13, $14, $15, $16, $17, NULL)
            """,
            [
                Db.Param(employeeId),
                Db.Param(request.Number),
                Db.Param(request.Kind),
                Db.Param(request.ContractType),
                Db.Param(request.SignedAt),
                Db.Param(request.StartDate),
                Db.Param(request.EndDate),
                .. EmploymentTermsFields.Params(request),
            ],
            cancellationToken
        );
}
