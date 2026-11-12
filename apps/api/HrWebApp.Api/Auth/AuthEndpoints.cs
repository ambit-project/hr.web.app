using HrWebApp.Api.Database;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace HrWebApp.Api.Auth;

public static class AuthEndpoints
{
    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public sealed record LoginRequest(string Login, string Password);

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public sealed record DevLoginRequest(string Role);

    public sealed record LoginResponse(string Token, DateTimeOffset ExpiresAt);

    public sealed record OptionsResponse(bool DevLogin);

    public sealed record MeResponse(
        Guid Id,
        string Login,
        string Email,
        string DisplayName,
        IReadOnlyList<string> Roles,
        IReadOnlyList<ProfileResponse> Profiles
    );

    /// <summary>Profil pracownika w spółce: kartoteka powiązana z kontem, po jednej na spółkę.</summary>
    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public sealed record ProfileResponse(Guid EmployeeId, Guid CompanyId, string CompanyShortName);

    public static bool IsDevLoginEnabled(IHostEnvironment environment, AuthOptions options) =>
        environment.IsDevelopment() && options.DevLogin;

    public static IEndpointRouteBuilder MapAuth(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth");
        group.MapPost("/login", LoginAsync).WithName("Login").AllowAnonymous();
        group.MapPost("/logout", LogoutAsync).WithName("Logout");
        group.MapGet("/me", MeAsync).WithName("Me");
        group.MapGet("/options", GetAuthOptions).WithName("AuthOptions").AllowAnonymous();

        var environment = app.ServiceProvider.GetRequiredService<IHostEnvironment>();
        var options = app.ServiceProvider.GetRequiredService<IOptions<AuthOptions>>().Value;
        if (IsDevLoginEnabled(environment, options))
        {
            group.MapPost("/dev-login", DevLoginAsync).WithName("DevLogin").AllowAnonymous();
        }

        return app;
    }

    public static async Task<Results<Ok<LoginResponse>, ProblemHttpResult>> LoginAsync(
        LoginRequest request,
        DbSession db,
        IOptions<AuthOptions> options,
        CancellationToken cancellationToken
    )
    {
        var auth = options.Value;
        var token = SessionToken.New();
        var userId = await db.ScalarAsync<Guid?>(
            "CALL platform_app.session_login($1, $2, $3, $4, $5, $6, NULL)",
            [
                Db.Param(request.Login),
                Db.Param(request.Password),
                Db.Param(token),
                Db.Param(auth.SessionHours),
                Db.Param(auth.MaxFailedAttempts),
                Db.Param(auth.LockoutMinutes),
            ],
            cancellationToken
        );
        return userId is null ? Unauthorized() : TypedResults.Ok(Session(token, auth));
    }

    public static async Task<Results<Ok<LoginResponse>, ProblemHttpResult>> DevLoginAsync(
        DevLoginRequest request,
        DbSession db,
        IOptions<AuthOptions> options,
        CancellationToken cancellationToken
    )
    {
        var token = SessionToken.New();
        var userId = await db.ScalarAsync<Guid?>(
            "CALL platform_app.session_dev_login($1, $2, $3, NULL)",
            [Db.Param(request.Role), Db.Param(token), Db.Param(options.Value.SessionHours)],
            cancellationToken
        );
        return userId is null ? Unauthorized() : TypedResults.Ok(Session(token, options.Value));
    }

    public static async Task<NoContent> LogoutAsync(
        HttpRequest request,
        DbSession db,
        CancellationToken cancellationToken
    )
    {
        await db.ScalarAsync<object>(
            "CALL platform_app.session_logout($1)",
            [Db.Param(SessionToken.FromHeader(request) ?? "")],
            cancellationToken
        );
        return TypedResults.NoContent();
    }

    public static async Task<Results<Ok<MeResponse>, UnauthorizedHttpResult>> MeAsync(
        DbSession db,
        CancellationToken cancellationToken
    )
    {
        var rows = await db.QueryAsync(
            "SELECT id, login, email, display_name, roles FROM platform_app.me()",
            [],
            reader => new MeResponse(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetFieldValue<string[]>(4),
                []
            ),
            cancellationToken
        );
        if (rows.Count != 1)
        {
            return TypedResults.Unauthorized();
        }

        var profiles = await db.QueryAsync(
            "SELECT employee_id, company_id, company_short_name FROM platform_app.my_profiles()",
            [],
            reader => new ProfileResponse(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2)
            ),
            cancellationToken
        );
        return TypedResults.Ok(rows[0] with { Profiles = profiles });
    }

    public static Ok<OptionsResponse> GetAuthOptions(
        IHostEnvironment environment,
        IOptions<AuthOptions> options
    ) => TypedResults.Ok(new OptionsResponse(IsDevLoginEnabled(environment, options.Value)));

    private static LoginResponse Session(string token, AuthOptions options) =>
        new(token, DateTimeOffset.UtcNow.AddHours(options.SessionHours));

    // Ten sam błąd dla złego hasła, nieznanego loginu i blokady: komunikat nie zdradza, czy konto istnieje (R4).
    private static ProblemHttpResult Unauthorized() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            extensions: new Dictionary<string, object?> { ["code"] = DbErrors.AuthFailed }
        );
}
