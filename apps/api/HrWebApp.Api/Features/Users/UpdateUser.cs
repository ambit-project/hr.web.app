using System.ComponentModel.DataAnnotations;
using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HrWebApp.Api.Features.Users;

public static class UpdateUser
{
    /// <summary>Login się nie zmienia; zmiana ról i aktywności z kontrolą wersji wiersza.</summary>
    public sealed record Request(
        long RowVersion,
        [property: MaxLength(200)] string Email,
        [property: MaxLength(200)] string DisplayName,
        IReadOnlyList<string> Roles,
        bool IsActive
    );

    public static async Task<NoContent> Handle(
        Guid id,
        Request request,
        UpdateUserHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.HandleAsync(id, request, cancellationToken);
        return TypedResults.NoContent();
    }
}

public sealed class UpdateUserHandler(DbSession db)
{
    public async Task HandleAsync(
        Guid id,
        UpdateUser.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<object>(
            "CALL platform_app.user_update($1, $2, $3, $4, $5, $6)",
            [
                Db.Param(id),
                Db.Param(request.RowVersion),
                Db.Param(request.Email),
                Db.Param(request.DisplayName),
                Db.Param(request.Roles.ToArray()),
                Db.Param(request.IsActive),
            ],
            cancellationToken
        );
}
