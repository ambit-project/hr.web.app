using System.ComponentModel.DataAnnotations;
using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HrWebApp.Api.Features.ZusCodes;

public static class UpdateZusCode
{
    /// <summary>Rodzaj i kod są niezmienne; zmienia się nazwa i okres obowiązywania.</summary>
    public sealed record Request(
        long RowVersion,
        [property: MaxLength(2000)] string Name,
        DateOnly? ValidFrom,
        DateOnly? ValidTo
    );

    public static async Task<NoContent> Handle(
        Guid id,
        Request request,
        UpdateZusCodeHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.HandleAsync(id, request, cancellationToken);
        return TypedResults.NoContent();
    }
}

public sealed class UpdateZusCodeHandler(DbSession db)
{
    public async Task HandleAsync(
        Guid id,
        UpdateZusCode.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<object>(
            "CALL dictionaries_app.zus_code_update($1, $2, $3, $4, $5)",
            [
                Db.Param(id),
                Db.Param(request.RowVersion),
                Db.Param(request.Name),
                Db.Param(request.ValidFrom),
                Db.Param(request.ValidTo),
            ],
            cancellationToken
        );
}
