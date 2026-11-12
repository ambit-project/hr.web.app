using System.ComponentModel.DataAnnotations;
using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HrWebApp.Api.Features.Companies;

public static class UpdateCompany
{
    /// <summary>RowVersion z listy; inna wersja w bazie = ktoś zmienił spółkę w międzyczasie (409).</summary>
    public sealed record Request(
        long RowVersion,
        [property: MaxLength(50)] string ShortName,
        [property: MaxLength(300)] string Name,
        string Nip,
        string Regon,
        string Street,
        string PostalCode,
        string City,
        bool IsActive
    );

    public static async Task<NoContent> Handle(
        Guid id,
        Request request,
        UpdateCompanyHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.HandleAsync(id, request, cancellationToken);
        return TypedResults.NoContent();
    }
}

public sealed class UpdateCompanyHandler(DbSession db)
{
    public async Task HandleAsync(
        Guid id,
        UpdateCompany.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<object>(
            "CALL platform_app.company_update($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)",
            [
                Db.Param(id),
                Db.Param(request.RowVersion),
                Db.Param(request.ShortName),
                Db.Param(request.Name),
                Db.Param(request.Nip),
                Db.Param(request.Regon),
                Db.Param(request.Street),
                Db.Param(request.PostalCode),
                Db.Param(request.City),
                Db.Param(request.IsActive),
            ],
            cancellationToken
        );
}
