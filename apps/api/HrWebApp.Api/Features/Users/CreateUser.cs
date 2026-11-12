using System.ComponentModel.DataAnnotations;
using HrWebApp.Api.Auth;
using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace HrWebApp.Api.Features.Users;

public static class CreateUser
{
    public sealed record Request(
        [property: MaxLength(100)] string Login,
        [property: MaxLength(200)] string Email,
        [property: MaxLength(200)] string DisplayName,
        IReadOnlyList<string> Roles,
        string Password
    );

    public sealed record Response(Guid Id);

    public static async Task<Created<Response>> Handle(
        Request request,
        CreateUserHandler handler,
        CancellationToken cancellationToken
    )
    {
        var id = await handler.HandleAsync(request, cancellationToken);
        return TypedResults.Created($"/api/users/{id}", new Response(id));
    }
}

public sealed class CreateUserHandler(DbSession db, IOptions<AuthOptions> options)
{
    public async Task<Guid> HandleAsync(
        CreateUser.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<Guid>(
            "CALL platform_app.user_create($1, $2, $3, $4, $5, $6, NULL)",
            [
                Db.Param(request.Login),
                Db.Param(request.Email),
                Db.Param(request.DisplayName),
                Db.Param(request.Roles.ToArray()),
                Db.Param(request.Password),
                Db.Param(options.Value.MinPasswordLength),
            ],
            cancellationToken
        );
}
