namespace HrWebApp.Api.Database;

/// <summary>Katalog kodów błędów z bazy (SQLSTATE U…). Opis: db/README.md.</summary>
public static class DbErrors
{
    public const string Validation = "U0001";
    private const string NotFound = "U0002";
    private const string Conflict = "U0003";
    private const string Forbidden = "U0004";

    public static int? StatusCode(string sqlState) =>
        sqlState switch
        {
            Validation => StatusCodes.Status400BadRequest,
            NotFound => StatusCodes.Status404NotFound,
            Conflict => StatusCodes.Status409Conflict,
            Forbidden => StatusCodes.Status403Forbidden,
            _ => null,
        };
}
