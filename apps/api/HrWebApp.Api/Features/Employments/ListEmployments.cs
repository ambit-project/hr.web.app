using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HrWebApp.Api.Features.Employments;

public static class ListEmployments
{
    /// <summary>
    /// Kind: <c>employment</c> (umowa o pracę), <c>mandate</c> (zlecenie), <c>specific_work</c> (dzieło).
    /// ContractType (umowa o pracę): <c>trial</c>, <c>fixed_term</c>,
    /// <c>indefinite</c>. Status: <c>future</c>, <c>active</c>, <c>ended</c>. Bieżące warunki bez kwot — kwoty
    /// są w szczegółach umowy, których odczyt jest audytowany.
    /// </summary>
    public sealed record Item(
        Guid Id,
        string Number,
        string Kind,
        string? ContractType,
        DateOnly SignedAt,
        DateOnly StartDate,
        DateOnly? EndDate,
        DateOnly? TerminatedAt,
        string Status,
        DateOnly? RetentionUntil,
        string? PositionName,
        int? FteNumerator,
        int? FteDenominator,
        string? PayBasis
    );

    public static async Task<Ok<List<Item>>> Handle(
        Guid id,
        ListEmploymentsHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.HandleAsync(id, cancellationToken));
}

public sealed class ListEmploymentsHandler(DbSession db)
{
    public async Task<List<ListEmployments.Item>> HandleAsync(
        Guid employeeId,
        CancellationToken cancellationToken
    ) =>
        await db.QueryAsync(
            """
            SELECT id, number, kind, contract_type, signed_at, start_date, end_date, terminated_at, status,
                   retention_until, position_name, fte_numerator, fte_denominator, pay_basis
            FROM employment_app.employment_list($1)
            """,
            [Db.Param(employeeId)],
            reader => new ListEmployments.Item(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetFieldValue<DateOnly>(4),
                reader.GetFieldValue<DateOnly>(5),
                reader.IsDBNull(6) ? null : reader.GetFieldValue<DateOnly>(6),
                reader.IsDBNull(7) ? null : reader.GetFieldValue<DateOnly>(7),
                reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetFieldValue<DateOnly>(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetInt32(11),
                reader.IsDBNull(12) ? null : reader.GetInt32(12),
                reader.IsDBNull(13) ? null : reader.GetString(13)
            ),
            cancellationToken
        );
}
