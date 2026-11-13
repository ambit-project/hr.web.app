using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Npgsql;

namespace HrWebApp.Api.Common.Errors;

/// <summary>
/// Ostatnia linia obsługi błędów (wzór: ambit.es): wszystko, czego nie przetłumaczył <c>DbErrorHandler</c>, kończy się
/// ProblemDetails z <c>traceId</c> i wpisem w logu pod tym samym identyfikatorem. Szczegóły wyjątku (komunikat, typ)
/// widać tylko w Development — gdzie indziej komunikat wyjątku mógłby zdradzić nazwy typów, tabel albo dane.
/// </summary>
public sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetails,
    IHostEnvironment environment,
    ILogger<GlobalExceptionHandler> logger
) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        // Klient przerwał żądanie — nie ma komu odpowiedzieć i to nie jest błąd aplikacji.
        if (
            exception is OperationCanceledException
            && httpContext.RequestAborted.IsCancellationRequested
        )
        {
            return true;
        }

        var (status, title, detail) = Classify(exception);
        if (status >= StatusCodes.Status500InternalServerError)
        {
            LogFailed(
                logger,
                exception,
                status,
                Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier
            );
        }
        else
        {
            LogRejected(logger, status, title);
        }

        httpContext.Response.StatusCode = status;
        var problem = new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
        };
        if (environment.IsDevelopment())
        {
            problem.Extensions["exception"] = exception.GetType().Name;
        }

        return await problemDetails.TryWriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = problem,
                Exception = exception,
            }
        );
    }

    private (int Status, string Title, string? Detail) Classify(Exception exception) =>
        exception switch
        {
            // Zły JSON, brak treści, za duże żądanie — błąd klienta, nie serwera.
            BadHttpRequestException bad => (bad.StatusCode, "Invalid request", Internal(bad)),
            // Baza nie odpowiada albo odrzuca połączenia: warto spróbować ponownie, nic nie trzeba poprawiać.
            NpgsqlException npgsql when IsUnavailable(npgsql) => (
                StatusCodes.Status503ServiceUnavailable,
                "Database unavailable",
                "The database did not answer — try again in a moment."
            ),
            _ => (
                StatusCodes.Status500InternalServerError,
                "Internal server error",
                Internal(exception) ?? "An unexpected error occurred."
            ),
        };

    /// <summary>Komunikat wyjątku tylko w Development.</summary>
    private string? Internal(Exception exception) =>
        environment.IsDevelopment() ? exception.Message : null;

    /// <summary>
    /// Połączenie (klasa 08), brak zasobów (53), wyłączanie serwera (57P01–57P03) albo błąd sieci bez kodu SQLSTATE.
    /// </summary>
    private static bool IsUnavailable(NpgsqlException exception) =>
        exception is PostgresException postgres
            ? postgres.SqlState.StartsWith("08", StringComparison.Ordinal)
                || postgres.SqlState.StartsWith("53", StringComparison.Ordinal)
                || postgres.SqlState is "57P01" or "57P02" or "57P03"
            : exception.IsTransient
                || exception.InnerException is System.Net.Sockets.SocketException;

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Request failed with {Status}. TraceId: {TraceId}"
    )]
    private static partial void LogFailed(
        ILogger logger,
        Exception exception,
        int status,
        string traceId
    );

    [LoggerMessage(Level = LogLevel.Warning, Message = "Request rejected with {Status}: {Title}")]
    private static partial void LogRejected(ILogger logger, int status, string title);
}
