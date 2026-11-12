using System.ComponentModel.DataAnnotations;
using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HrWebApp.Api.Features.Positions;

public static class UpdatePosition
{
    /// <summary>RowVersion z listy; inna wersja w bazie = ktoś zmienił stanowisko w międzyczasie (409).</summary>
    public sealed record Request(
        long RowVersion,
        [property: MaxLength(200)] string Name,
        [property: MaxLength(1000)] string? Description,
        bool IsActive
    );

    public static async Task<NoContent> Handle(
        Guid id,
        Request request,
        UpdatePositionHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.HandleAsync(id, request, cancellationToken);
        return TypedResults.NoContent();
    }
}

public sealed class UpdatePositionHandler(DbSession db)
{
    public async Task HandleAsync(
        Guid id,
        UpdatePosition.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<object>(
            "CALL dictionaries_app.position_update($1, $2, $3, $4, $5)",
            [
                Db.Param(id),
                Db.Param(request.RowVersion),
                Db.Param(request.Name),
                Db.Param(request.Description),
                Db.Param(request.IsActive),
            ],
            cancellationToken
        );
}
