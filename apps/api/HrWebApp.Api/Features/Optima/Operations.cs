using System.Text.Json;
using HrWebApp.Api.Common;
using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace HrWebApp.Api.Features.Optima;

public static class CreateOperation
{
    /// <summary>
    /// Kind: ReadPayrollComponents (wszystkie bazy, bez companyId), AddEmployees (companyId; employeeIds albo
    /// wszystkie kartoteki z umową niezakończoną przed dziś), ExportDeductions, ExportAllowances, ReadPayrolls
    /// (companyId, year, month). Spółka musi być powiązana z bazą Optimy (400 notLinked; 409 taxNumberMismatch).
    /// </summary>
    public sealed record Request(
        string? Kind,
        Guid? CompanyId,
        int? Year,
        int? Month,
        IReadOnlyList<Guid>? EmployeeIds
    );

    public sealed record Response(Guid Id);

    public static async Task<Created<Response>> Handle(
        Request request,
        OperationsHandler handler,
        CancellationToken cancellationToken
    )
    {
        var id = await handler.CreateAsync(request, cancellationToken);
        return TypedResults.Created($"/api/optima/operations/{id}", new Response(id));
    }
}

public static class ListOperations
{
    public sealed record Query(
        [FromQuery(Name = "kind")] string? Kind,
        [FromQuery(Name = "state")] string? State,
        [FromQuery(Name = "companyId")] Guid? CompanyId,
        [FromQuery(Name = "page")] int? Page,
        [FromQuery(Name = "pageSize")] int? PageSize
    );

    /// <summary>State: pending, claimed (agent pracuje albo nie odpowiedział), succeeded, failed.</summary>
    public sealed record Item(
        Guid Id,
        string Kind,
        string State,
        int Attempt,
        Guid? CompanyId,
        string? CompanyShortName,
        string? DatabaseName,
        int? Year,
        int? Month,
        DateTimeOffset CreatedAt,
        DateTimeOffset? ClaimedAt,
        DateTimeOffset? FinishedAt,
        string? ErrorCode
    );

    public static async Task<Ok<PagedList<Item>>> Handle(
        [AsParameters] Query query,
        OperationsHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.ListAsync(query, cancellationToken));
}

public static class GetOperation
{
    /// <summary>Krok logu agenta: kody i identyfikatory (tekst buduje front po kodzie).</summary>
    public sealed record LogEntry(
        DateTimeOffset? At,
        string? Code,
        string? DatabaseName,
        int? ErrorNumber,
        int? Count,
        Guid? EmployeeId,
        Guid? DeductionId,
        Guid? AllowanceId,
        string? WarningCodes
    );

    /// <summary>Pozycja pominięta przy zleceniu. Reason: notFixed, notMapped, payTypeInactive.</summary>
    public sealed record Skipped(Guid Id, Guid EmployeeId, string Reason);

    public sealed record Response(
        Guid Id,
        string Kind,
        string State,
        int Attempt,
        Guid? CompanyId,
        string? CompanyShortName,
        string? DatabaseName,
        int? Year,
        int? Month,
        int? EmployeeCount,
        DateTimeOffset CreatedAt,
        DateTimeOffset? ClaimedAt,
        DateTimeOffset? FinishedAt,
        bool? Succeeded,
        string? ErrorCode,
        IReadOnlyList<LogEntry> Log,
        IReadOnlyList<Skipped> SkippedItems
    );

    public static async Task<Ok<Response>> Handle(
        Guid id,
        OperationsHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.GetAsync(id, cancellationToken));
}

public static class RetryOperation
{
    public sealed record Response(int Attempt);

    public static async Task<Ok<Response>> Handle(
        Guid id,
        OperationsHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(new Response(await handler.RetryAsync(id, cancellationToken)));
}

public sealed class OperationsHandler(DbSession db)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Guid> CreateAsync(
        CreateOperation.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<Guid>(
            "CALL optima_app.operation_create($1, $2, $3, $4, $5, NULL)",
            [
                Db.Param(request.Kind),
                Db.Param(request.CompanyId),
                Db.Param(request.Year),
                Db.Param(request.Month),
                Db.Param(request.EmployeeIds?.ToArray()),
            ],
            cancellationToken
        );

    public async Task<PagedList<ListOperations.Item>> ListAsync(
        ListOperations.Query query,
        CancellationToken cancellationToken
    )
    {
        var page = Paging.Number(query.Page);
        var size = Paging.Size(query.PageSize);
        long total = 0;
        var items = await db.QueryAsync(
            """
            SELECT id, kind, state, attempt, company_id, company_short_name, database_name, year, month,
                   created_at, claimed_at, finished_at, error_code, total_count
            FROM optima_app.operation_list($1, $2, $3, $4, $5)
            """,
            [
                Db.Param(query.Kind),
                Db.Param(query.State),
                Db.Param(query.CompanyId),
                Db.Param(size),
                Db.Param(Paging.Offset(page, size)),
            ],
            reader =>
            {
                total = reader.GetInt64(13);
                return new ListOperations.Item(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt32(3),
                    reader.IsDBNull(4) ? null : reader.GetGuid(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetInt32(7),
                    reader.IsDBNull(8) ? null : reader.GetInt32(8),
                    reader.GetFieldValue<DateTimeOffset>(9),
                    reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
                    reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset>(11),
                    reader.IsDBNull(12) ? null : reader.GetString(12)
                );
            },
            cancellationToken
        );
        return new PagedList<ListOperations.Item>(items, total, page, size);
    }

    public async Task<GetOperation.Response> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var rows = await db.QueryAsync(
            """
            SELECT id, kind, state, attempt, company_id, company_short_name, database_name, year, month,
                   employee_count, created_at, claimed_at, finished_at, succeeded, error_code, log::text, skipped::text
            FROM optima_app.operation_get($1)
            """,
            [Db.Param(id)],
            reader => new GetOperation.Response(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetGuid(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7),
                reader.IsDBNull(8) ? null : reader.GetInt32(8),
                reader.IsDBNull(9) ? null : reader.GetInt32(9),
                reader.GetFieldValue<DateTimeOffset>(10),
                reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset>(11),
                reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12),
                reader.IsDBNull(13) ? null : reader.GetBoolean(13),
                reader.IsDBNull(14) ? null : reader.GetString(14),
                ReadLog(reader.GetString(15)),
                JsonSerializer.Deserialize<List<GetOperation.Skipped>>(reader.GetString(16), Json)
                    ?? []
            ),
            cancellationToken
        );
        return rows.Single();
    }

    public async Task<int> RetryAsync(Guid id, CancellationToken cancellationToken) =>
        await db.ScalarAsync<int>(
            "CALL optima_app.operation_retry($1, NULL)",
            [Db.Param(id)],
            cancellationToken
        );

    /// <summary>Log pisze agent — wpis, którego nie da się odczytać, jest pomijany, a nie psuje podglądu.</summary>
    private static List<GetOperation.LogEntry> ReadLog(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<GetOperation.LogEntry>>(json, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
