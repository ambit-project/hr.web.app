using System.ComponentModel.DataAnnotations;
using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HrWebApp.Api.Features.ZusCodes;

public static class CreateZusCode
{
    /// <summary>Format kodu dla rodzaju i unikalność sprawdza procedura.</summary>
    public sealed record Request(
        string Kind,
        [property: MaxLength(4)] string Code,
        [property: MaxLength(2000)] string Name,
        DateOnly? ValidFrom,
        DateOnly? ValidTo
    );

    public sealed record Response(Guid Id);

    public static async Task<Created<Response>> Handle(
        Request request,
        CreateZusCodeHandler handler,
        CancellationToken cancellationToken
    )
    {
        var id = await handler.HandleAsync(request, cancellationToken);
        return TypedResults.Created($"/api/zus-codes/{id}", new Response(id));
    }
}

public sealed class CreateZusCodeHandler(DbSession db)
{
    public async Task<Guid> HandleAsync(
        CreateZusCode.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<Guid>(
            "CALL dictionaries_app.zus_code_create($1, $2, $3, $4, $5, NULL)",
            [
                Db.Param(request.Kind),
                Db.Param(request.Code),
                Db.Param(request.Name),
                Db.Param(request.ValidFrom),
                Db.Param(request.ValidTo),
            ],
            cancellationToken
        );
}
