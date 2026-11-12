using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Npgsql;

namespace HrWebApp.Api.Database;

/// <summary>
/// Tłumaczy błędy z rutyn (SQLSTATE U…) na ProblemDetails. Kod trafia do rozszerzenia <c>code</c>,
/// a przy U0001 pola z HINT do <c>errors</c> ({"pole": ["kod"]}) — front tłumaczy po kodach.
/// </summary>
public sealed class DbErrorHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        if (exception is not PostgresException { SqlState: var sqlState } postgres)
        {
            return false;
        }

        var status = DbErrors.StatusCode(sqlState);
        if (status is null)
        {
            return false;
        }

        httpContext.Response.StatusCode = status.Value;
        var problem =
            sqlState == DbErrors.Validation
                ? new HttpValidationProblemDetails(ParseFieldErrors(postgres.Hint))
                : new Microsoft.AspNetCore.Mvc.ProblemDetails();
        problem.Status = status.Value;
        problem.Extensions["code"] = sqlState;

        return await problemDetails.TryWriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = problem,
                Exception = exception,
            }
        );
    }

    private static Dictionary<string, string[]> ParseFieldErrors(string? hint)
    {
        if (string.IsNullOrWhiteSpace(hint))
        {
            return [];
        }

        return JsonSerializer.Deserialize<Dictionary<string, string[]>>(hint) ?? [];
    }
}
