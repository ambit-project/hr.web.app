using System.ComponentModel.DataAnnotations;
using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HrWebApp.Api.Features.TaxOffices;

public static class CreateTaxOffice
{
    /// <summary>Kod (4 cyfry) i unikalność sprawdza procedura.</summary>
    public sealed record Request(
        [property: MaxLength(4)] string Code,
        [property: MaxLength(300)] string Name
    );

    public sealed record Response(Guid Id);

    public static async Task<Created<Response>> Handle(
        Request request,
        CreateTaxOfficeHandler handler,
        CancellationToken cancellationToken
    )
    {
        var id = await handler.HandleAsync(request, cancellationToken);
        return TypedResults.Created($"/api/tax-offices/{id}", new Response(id));
    }
}

public sealed class CreateTaxOfficeHandler(DbSession db)
{
    public async Task<Guid> HandleAsync(
        CreateTaxOffice.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<Guid>(
            "CALL dictionaries_app.tax_office_create($1, $2, NULL)",
            [Db.Param(request.Code), Db.Param(request.Name)],
            cancellationToken
        );
}
