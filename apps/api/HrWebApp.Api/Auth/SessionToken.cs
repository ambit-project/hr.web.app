using System.Security.Cryptography;

namespace HrWebApp.Api.Auth;

public static class SessionToken
{
    /// <summary>32 losowe bajty jako base64url; baza trzyma tylko skrót SHA-256.</summary>
    public static string New() => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static string? FromHeader(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        return header.StartsWith(prefix, StringComparison.Ordinal) && header.Length > prefix.Length
            ? header[prefix.Length..].Trim()
            : null;
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
