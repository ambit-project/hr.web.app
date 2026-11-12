namespace HrWebApp.Api.Database;

/// <summary>Katalog kodów błędów z bazy (SQLSTATE U…). Opis: db/README.md.</summary>
public static class DbErrors
{
    public const string Validation = "U0001";
    private const string NotFound = "U0002";
    private const string Conflict = "U0003";
    private const string Forbidden = "U0004";
    private const string MissingLegalParameter = "U0006";

    /// <summary>Logowanie nieudane (złe dane, blokada, nieaktywne konto) — zwraca API, nie baza.</summary>
    public const string AuthFailed = "U0005";

    public static int? StatusCode(string sqlState) =>
        sqlState switch
        {
            Validation => StatusCodes.Status400BadRequest,
            NotFound => StatusCodes.Status404NotFound,
            Conflict => StatusCodes.Status409Conflict,
            Forbidden => StatusCodes.Status403Forbidden,
            MissingLegalParameter => StatusCodes.Status409Conflict,
            _ => null,
        };
}
