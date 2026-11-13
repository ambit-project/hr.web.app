using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Npgsql;

namespace HrWebApp.Api.Features.Optima.Agent;

/// <summary>
/// Schemat „OptimaAgent”: token agenta w nagłówku <c>X-Optima-Agent-Key</c> (<c>optima_</c> + 64 znaki hex). Baza zna
/// tylko skrót SHA-256 tokenu; rutyny agenta dostają ten skrót i same sprawdzają klucz. Schemat nie jest domyślny:
/// endpointy użytkowników go nie przyjmują, a endpointy agenta nie przyjmują sesji (ADR-0010).
/// </summary>
public sealed class AgentKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    NpgsqlDataSource dataSource
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "OptimaAgent";
    public const string PolicyName = "OptimaAgent";
    public const string HeaderName = "X-Optima-Agent-Key";
    private const string KeyHashItem = "optima.agentKeyHash";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var values))
        {
            return AuthenticateResult.NoResult();
        }

        var token = values.ToString();
        if (!AgentToken.IsWellFormed(token))
        {
            return AuthenticateResult.Fail("Malformed agent key.");
        }

        var hash = AgentToken.Hash(token);
        await using var command = dataSource.CreateCommand(
            "SELECT optima_app.agent_key_resolve($1)"
        );
        command.Parameters.Add(Db.Param(hash));
        var keyId = await command.ExecuteScalarAsync(Context.RequestAborted);
        if (keyId is not Guid id)
        {
            return AuthenticateResult.Fail("Unknown or revoked agent key.");
        }

        Context.Items[KeyHashItem] = hash;
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], SchemeName)
        );
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    /// <summary>Skrót tokenu uwierzytelnionego agenta (ustawia handler przed endpointem).</summary>
    public static byte[] KeyHash(HttpContext context) =>
        context.Items[KeyHashItem] as byte[]
        ?? throw new InvalidOperationException("The request is not authenticated as an agent.");
}

public static partial class AgentToken
{
    private const string Prefix = "optima_";

    /// <summary>Nowy token: 256 bitów losowości, małe litery hex (format, którego oczekuje integrator).</summary>
    public static string New() =>
        Prefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    public static bool IsWellFormed(string token) => TokenPattern().IsMatch(token);

    public static byte[] Hash(string token) => SHA256.HashData(Encoding.ASCII.GetBytes(token));

    /// <summary>Początek tokenu do rozpoznania klucza na liście (bez ujawniania reszty).</summary>
    public static string DisplayPrefix(string token) => token[..(Prefix.Length + 6)];

    [GeneratedRegex("^optima_[0-9a-f]{64}$")]
    private static partial Regex TokenPattern();
}
