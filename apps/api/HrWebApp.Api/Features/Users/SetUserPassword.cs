using HrWebApp.Api.Auth;
using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace HrWebApp.Api.Features.Users;

public static class SetUserPassword
{
    public sealed record Request(string Password);

    public static async Task<NoContent> Handle(
        Guid id,
        Request request,
        SetUserPasswordHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.HandleAsync(id, request, cancellationToken);
        return TypedResults.NoContent();
    }
}

public sealed class SetUserPasswordHandler(DbSession db, IOptions<AuthOptions> options)
{
    public async Task HandleAsync(
        Guid id,
        SetUserPassword.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<object>(
            "CALL platform_app.user_set_password($1, $2, $3)",
            [Db.Param(id), Db.Param(request.Password), Db.Param(options.Value.MinPasswordLength)],
            cancellationToken
        );
}
