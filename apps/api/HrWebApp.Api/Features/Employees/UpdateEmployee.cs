using System.ComponentModel.DataAnnotations;
using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HrWebApp.Api.Features.Employees;

public static class UpdateEmployee
{
    /// <summary>RowVersion ze szczegółów kartoteki; spółki nie zmienia się.</summary>
    public sealed record Request(
        long RowVersion,
        [property: MaxLength(20)] string Number,
        [property: MaxLength(30)] string Acronym,
        [property: MaxLength(100)] string FirstName,
        [property: MaxLength(100)] string? SecondName,
        [property: MaxLength(100)] string LastName,
        string? Pesel,
        [property: MaxLength(100)] string? IdentityDocument,
        DateOnly? BirthDate,
        Guid? TaxOfficeId,
        [property: MaxLength(200)] string? Email
    );

    public static async Task<NoContent> Handle(
        Guid id,
        Request request,
        UpdateEmployeeHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.HandleAsync(id, request, cancellationToken);
        return TypedResults.NoContent();
    }
}

public sealed class UpdateEmployeeHandler(DbSession db)
{
    public async Task HandleAsync(
        Guid id,
        UpdateEmployee.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<object>(
            "CALL employees_app.employee_update($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12)",
            [
                Db.Param(id),
                Db.Param(request.RowVersion),
                Db.Param(request.Number),
                Db.Param(request.Acronym),
                Db.Param(request.FirstName),
                Db.Param(request.SecondName),
                Db.Param(request.LastName),
                Db.Param(request.Pesel),
                Db.Param(request.IdentityDocument),
                Db.Param(request.BirthDate),
                Db.Param(request.TaxOfficeId),
                Db.Param(request.Email),
            ],
            cancellationToken
        );
}
