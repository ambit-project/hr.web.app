using System.ComponentModel.DataAnnotations;
using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HrWebApp.Api.Features.Positions;

public static class CreatePosition
{
    /// <summary>Reguły (wymagane, długość, unikalność) sprawdza procedura; atrybuty opisują kontrakt w OpenAPI.</summary>
    public sealed record Request(
        [property: MaxLength(200)] string Name,
        [property: MaxLength(1000)] string? Description
    );

    public sealed record Response(Guid Id);

    public static async Task<Created<Response>> Handle(
        Request request,
        CreatePositionHandler handler,
        CancellationToken cancellationToken
    )
    {
        var id = await handler.HandleAsync(request, cancellationToken);
        return TypedResults.Created($"/api/positions/{id}", new Response(id));
    }
}

public sealed class CreatePositionHandler(DbSession db)
{
    public async Task<Guid> HandleAsync(
        CreatePosition.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<Guid>(
            "CALL dictionaries_app.position_create($1, $2, NULL)",
            [Db.Param(request.Name), Db.Param(request.Description)],
            cancellationToken
        );
}
