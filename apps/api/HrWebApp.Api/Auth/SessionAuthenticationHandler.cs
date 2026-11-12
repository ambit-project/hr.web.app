using System.Security.Claims;
using System.Text.Encodings.Web;
using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Npgsql;

namespace HrWebApp.Api.Auth;

/// <summary>
/// Schemat „Session”: token z nagłówka Authorization (Bearer), który dokłada serwer frontu z ciasteczka httpOnly.
/// Sesję sprawdza baza (platform_app.session_resolve); id konta trafia do DbSession jako aktor.
/// </summary>
public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    NpgsqlDataSource dataSource
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Session";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = SessionToken.FromHeader(Request);
        if (token is null)
        {
            return AuthenticateResult.NoResult();
        }

        await using var command = dataSource.CreateCommand(
            "SELECT user_id, roles FROM platform_app.session_resolve($1)"
        );
        command.Parameters.Add(Db.Param(token));
        await using var reader = await command.ExecuteReaderAsync(Context.RequestAborted);
        if (!await reader.ReadAsync(Context.RequestAborted))
        {
            return AuthenticateResult.Fail("Invalid or expired session.");
        }

        var userId = reader.GetGuid(0);
        var roles = reader.GetFieldValue<string[]>(1);
        Context.RequestServices.GetRequiredService<DbSession>().ActorId = userId;

        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            .. roles.Select(role => new Claim(ClaimTypes.Role, role)),
        ];
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }
}
