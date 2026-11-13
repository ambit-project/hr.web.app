using System.Text.Json;
using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace HrWebApp.Api.Features.Optima;

public static class GetOptimaPayrolls
{
    public sealed record Query(
        [FromQuery(Name = "year")] int Year,
        [FromQuery(Name = "month")] int Month
    );

    public sealed record Element(int? ComponentOptimaId, string Name, decimal Amount);

    /// <summary>EmployeeId i EmployeeName puste, gdy pracownika z Optimy nie udało się rozpoznać w spółce.</summary>
    public sealed record Payout(
        int OptimaId,
        Guid? EmployeeId,
        string? EmployeeCode,
        string? EmployeeName,
        decimal? Gross,
        decimal? Net,
        bool IsClosed,
        IReadOnlyList<Element> Elements
    );

    public sealed record PayrollList(
        Guid Id,
        int OptimaId,
        string Number,
        string? Name,
        DateOnly? PeriodFrom,
        DateOnly? PeriodTo,
        DateOnly? DocumentDate,
        bool IsClosed,
        DateTimeOffset ImportedAt,
        IReadOnlyList<Payout> Payouts
    );

    /// <summary>Listy płac pobrane z Optimy (tylko do odczytu; pobiera je operacja ReadPayrolls).</summary>
    public static async Task<Ok<List<PayrollList>>> Handle(
        Guid companyId,
        [AsParameters] Query query,
        OptimaPayrollsHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.GetAsync(companyId, query, cancellationToken));
}

public sealed class OptimaPayrollsHandler(DbSession db)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<List<GetOptimaPayrolls.PayrollList>> GetAsync(
        Guid companyId,
        GetOptimaPayrolls.Query query,
        CancellationToken cancellationToken
    )
    {
        var json = await db.ScalarAsync<string>(
            "SELECT optima_app.payroll_get($1, $2, $3)::text",
            [Db.Param(companyId), Db.Param(query.Year), Db.Param(query.Month)],
            cancellationToken
        );
        return JsonSerializer.Deserialize<List<GetOptimaPayrolls.PayrollList>>(json ?? "[]", Json)
            ?? [];
    }
}
