using System.ComponentModel.DataAnnotations;
using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HrWebApp.Api.Features.Employees;

public static class CreateEmployee
{
    /// <summary>
    /// Reguły (PESEL, data urodzenia z PESEL, unikalność w spółce, urząd) sprawdza procedura. Pusty numer = kolejny
    /// wolny numer w spółce. Adres = adres zamieszkania od <c>AddressFrom</c>.
    /// </summary>
    public sealed record Request(
        Guid? CompanyId,
        [property: MaxLength(20)] string? Number,
        [property: MaxLength(100)] string FirstName,
        [property: MaxLength(100)] string? SecondName,
        [property: MaxLength(100)] string LastName,
        string? Pesel,
        [property: MaxLength(100)] string? IdentityDocument,
        DateOnly? BirthDate,
        Guid? TaxOfficeId,
        [property: MaxLength(200)] string? Email,
        [property: MaxLength(200)] string Street,
        string PostalCode,
        [property: MaxLength(100)] string City,
        DateOnly? AddressFrom
    );

    public sealed record Response(Guid Id);

    public static async Task<Created<Response>> Handle(
        Request request,
        CreateEmployeeHandler handler,
        CancellationToken cancellationToken
    )
    {
        var id = await handler.HandleAsync(request, cancellationToken);
        return TypedResults.Created($"/api/employees/{id}", new Response(id));
    }
}

public sealed class CreateEmployeeHandler(DbSession db)
{
    public async Task<Guid> HandleAsync(
        CreateEmployee.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<Guid>(
            "CALL employees_app.employee_create($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, NULL)",
            [
                Db.Param(request.CompanyId),
                Db.Param(request.Number),
                Db.Param(request.FirstName),
                Db.Param(request.SecondName),
                Db.Param(request.LastName),
                Db.Param(request.Pesel),
                Db.Param(request.IdentityDocument),
                Db.Param(request.BirthDate),
                Db.Param(request.TaxOfficeId),
                Db.Param(request.Email),
                Db.Param(request.Street),
                Db.Param(request.PostalCode),
                Db.Param(request.City),
                Db.Param(request.AddressFrom),
            ],
            cancellationToken
        );
}
