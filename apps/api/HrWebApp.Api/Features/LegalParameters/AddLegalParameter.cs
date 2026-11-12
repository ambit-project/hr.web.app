using System.ComponentModel.DataAnnotations;
using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HrWebApp.Api.Features.LegalParameters;

public static class AddLegalParameter
{
    /// <summary>Nowa wartość od dnia; poprzedni okres kodu kończy się dzień wcześniej (reguła w procedurze).</summary>
    public sealed record Request(
        [property: MaxLength(100)] string Code,
        decimal? Value,
        DateOnly? ValidFrom,
        [property: MaxLength(500)] string Source
    );

    public sealed record Response(Guid Id);

    public static async Task<Ok<Response>> Handle(
        Request request,
        AddLegalParameterHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(new Response(await handler.HandleAsync(request, cancellationToken)));
}

public sealed class AddLegalParameterHandler(DbSession db)
{
    public async Task<Guid> HandleAsync(
        AddLegalParameter.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<Guid>(
            "CALL platform_app.legal_parameter_add($1, $2, $3, $4, NULL)",
            [
                Db.Param(request.Code),
                Db.Param(request.Value),
                Db.Param(request.ValidFrom),
                Db.Param(request.Source),
            ],
            cancellationToken
        );
}
