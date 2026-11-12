using System.ComponentModel.DataAnnotations;
using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HrWebApp.Api.Features.Employees;

public static class ListEmployeeAddresses
{
    /// <summary>Kind: <c>residence</c> (zamieszkania) albo <c>correspondence</c> (korespondencyjny).</summary>
    public sealed record Item(
        Guid Id,
        string Kind,
        string Street,
        string PostalCode,
        string City,
        DateOnly ValidFrom,
        DateOnly? ValidTo
    );

    public static async Task<Ok<List<Item>>> Handle(
        Guid id,
        EmployeeAddressesHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.ListAsync(id, cancellationToken));
}

public static class AddEmployeeAddress
{
    /// <summary>Poprzedni adres tego rodzaju kończy się dzień przed <c>ValidFrom</c>; ta sama data poprawia adres.</summary>
    public sealed record Request(
        string Kind,
        [property: MaxLength(200)] string Street,
        string PostalCode,
        [property: MaxLength(100)] string City,
        DateOnly? ValidFrom
    );

    public sealed record Response(Guid Id);

    public static async Task<Ok<Response>> Handle(
        Guid id,
        Request request,
        EmployeeAddressesHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(new Response(await handler.AddAsync(id, request, cancellationToken)));
}

public sealed class EmployeeAddressesHandler(DbSession db)
{
    public async Task<List<ListEmployeeAddresses.Item>> ListAsync(
        Guid employeeId,
        CancellationToken cancellationToken
    ) =>
        await db.QueryAsync(
            """
            SELECT id, kind, street, postal_code, city, valid_from, valid_to
            FROM employees_app.address_list($1)
            """,
            [Db.Param(employeeId)],
            reader => new ListEmployeeAddresses.Item(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetFieldValue<DateOnly>(5),
                reader.IsDBNull(6) ? null : reader.GetFieldValue<DateOnly>(6)
            ),
            cancellationToken
        );

    public async Task<Guid> AddAsync(
        Guid employeeId,
        AddEmployeeAddress.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<Guid>(
            "CALL employees_app.address_add($1, $2, $3, $4, $5, $6, NULL)",
            [
                Db.Param(employeeId),
                Db.Param(request.Kind),
                Db.Param(request.Street),
                Db.Param(request.PostalCode),
                Db.Param(request.City),
                Db.Param(request.ValidFrom),
            ],
            cancellationToken
        );
}
