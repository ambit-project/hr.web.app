using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HrWebApp.Api.Features.Employees;

public static class DeleteEmployee
{
    public static async Task<NoContent> Handle(
        Guid id,
        DeleteEmployeeHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.HandleAsync(id, cancellationToken);
        return TypedResults.NoContent();
    }
}

public sealed class DeleteEmployeeHandler(DbSession db)
{
    public async Task HandleAsync(Guid id, CancellationToken cancellationToken) =>
        await db.ScalarAsync<object>(
            "CALL employees_app.employee_delete($1)",
            [Db.Param(id)],
            cancellationToken
        );
}
