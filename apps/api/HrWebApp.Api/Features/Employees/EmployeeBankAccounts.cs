using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HrWebApp.Api.Features.Employees;

public static class ListEmployeeBankAccounts
{
    /// <summary>Numer zamaskowany: tylko 4 ostatnie cyfry. Pełny bieżący numer jest w szczegółach kartoteki.</summary>
    public sealed record Item(Guid Id, string Last4, DateOnly ValidFrom, DateOnly? ValidTo);

    public static async Task<Ok<List<Item>>> Handle(
        Guid id,
        EmployeeBankAccountsHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.ListAsync(id, cancellationToken));
}

public static class AddEmployeeBankAccount
{
    /// <summary>Polski IBAN (z <c>PL</c> albo same 26 cyfr); poprzedni rachunek kończy się dzień wcześniej.</summary>
    public sealed record Request(string Iban, DateOnly? ValidFrom);

    public sealed record Response(Guid Id);

    public static async Task<Ok<Response>> Handle(
        Guid id,
        Request request,
        EmployeeBankAccountsHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(new Response(await handler.AddAsync(id, request, cancellationToken)));
}

public static class CloseEmployeeBankAccount
{
    /// <summary>Od dnia po <c>ValidTo</c> wypłata gotówką.</summary>
    public sealed record Request(DateOnly? ValidTo);

    public static async Task<NoContent> Handle(
        Guid id,
        Request request,
        EmployeeBankAccountsHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.CloseAsync(id, request, cancellationToken);
        return TypedResults.NoContent();
    }
}

public sealed class EmployeeBankAccountsHandler(DbSession db)
{
    public async Task<List<ListEmployeeBankAccounts.Item>> ListAsync(
        Guid employeeId,
        CancellationToken cancellationToken
    ) =>
        await db.QueryAsync(
            "SELECT id, last4, valid_from, valid_to FROM employees_app.bank_account_list($1)",
            [Db.Param(employeeId)],
            reader => new ListEmployeeBankAccounts.Item(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetFieldValue<DateOnly>(2),
                reader.IsDBNull(3) ? null : reader.GetFieldValue<DateOnly>(3)
            ),
            cancellationToken
        );

    public async Task<Guid> AddAsync(
        Guid employeeId,
        AddEmployeeBankAccount.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<Guid>(
            "CALL employees_app.bank_account_add($1, $2, $3, NULL)",
            [Db.Param(employeeId), Db.Param(request.Iban), Db.Param(request.ValidFrom)],
            cancellationToken
        );

    public async Task CloseAsync(
        Guid employeeId,
        CloseEmployeeBankAccount.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<object>(
            "CALL employees_app.bank_account_close($1, $2)",
            [Db.Param(employeeId), Db.Param(request.ValidTo)],
            cancellationToken
        );
}
