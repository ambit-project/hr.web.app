using System.ComponentModel.DataAnnotations;
using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HrWebApp.Api.Features.TaxOffices;

public static class UpdateTaxOffice
{
    /// <summary>Kod jest niezmienny; zmienia się nazwa i aktywność.</summary>
    public sealed record Request(
        long RowVersion,
        [property: MaxLength(300)] string Name,
        bool IsActive
    );

    public static async Task<NoContent> Handle(
        Guid id,
        Request request,
        UpdateTaxOfficeHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.HandleAsync(id, request, cancellationToken);
        return TypedResults.NoContent();
    }
}

public sealed class UpdateTaxOfficeHandler(DbSession db)
{
    public async Task HandleAsync(
        Guid id,
        UpdateTaxOffice.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<object>(
            "CALL dictionaries_app.tax_office_update($1, $2, $3, $4)",
            [
                Db.Param(id),
                Db.Param(request.RowVersion),
                Db.Param(request.Name),
                Db.Param(request.IsActive),
            ],
            cancellationToken
        );
}
