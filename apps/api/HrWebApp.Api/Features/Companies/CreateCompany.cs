using System.ComponentModel.DataAnnotations;
using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HrWebApp.Api.Features.Companies;

public static class CreateCompany
{
    /// <summary>Reguły (NIP, REGON, unikalność) sprawdza procedura; atrybuty opisują kontrakt w OpenAPI.</summary>
    public sealed record Request(
        [property: MaxLength(50)] string ShortName,
        [property: MaxLength(300)] string Name,
        string Nip,
        string Regon,
        string Street,
        string PostalCode,
        string City
    );

    public sealed record Response(Guid Id);

    public static async Task<Created<Response>> Handle(
        Request request,
        CreateCompanyHandler handler,
        CancellationToken cancellationToken
    )
    {
        var id = await handler.HandleAsync(request, cancellationToken);
        return TypedResults.Created($"/api/companies/{id}", new Response(id));
    }
}

public sealed class CreateCompanyHandler(DbSession db)
{
    public async Task<Guid> HandleAsync(
        CreateCompany.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<Guid>(
            "CALL platform_app.company_create($1, $2, $3, $4, $5, $6, $7, NULL)",
            [
                Db.Param(request.ShortName),
                Db.Param(request.Name),
                Db.Param(request.Nip),
                Db.Param(request.Regon),
                Db.Param(request.Street),
                Db.Param(request.PostalCode),
                Db.Param(request.City),
            ],
            cancellationToken
        );
}
