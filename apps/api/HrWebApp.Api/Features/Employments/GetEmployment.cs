using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HrWebApp.Api.Features.Employments;

public static class GetEmployment
{
    /// <summary>Wersja warunków; ostatnia ma pusty <c>ValidTo</c> (obowiązuje do końca umowy).</summary>
    public sealed record Terms(
        Guid Id,
        DateOnly ValidFrom,
        DateOnly? ValidTo,
        Guid? PositionId,
        string? PositionName,
        int? FteNumerator,
        int? FteDenominator,
        string PayBasis,
        decimal BaseAmount,
        string? ZusTitleCode,
        string? ZusPensionCode,
        string? ZusDisabilityCode,
        string[] Insurances,
        string CostKind
    );

    /// <summary>Umowa z historią warunków (od najnowszej). Odczyt warunków zapisuje audyt odczytu w bazie.</summary>
    public sealed record Response(
        Guid Id,
        Guid EmployeeId,
        string EmployeeName,
        string CompanyShortName,
        string Number,
        string Kind,
        string? ContractType,
        DateOnly SignedAt,
        DateOnly StartDate,
        DateOnly? EndDate,
        DateOnly? TerminatedAt,
        string Status,
        DateOnly? RetentionUntil,
        long RowVersion,
        List<Terms> TermsHistory
    );

    public static async Task<Ok<Response>> Handle(
        Guid id,
        GetEmploymentHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.HandleAsync(id, cancellationToken));
}

public sealed class GetEmploymentHandler(DbSession db)
{
    public async Task<GetEmployment.Response> HandleAsync(
        Guid id,
        CancellationToken cancellationToken
    )
    {
        // Brak umowy zgłasza rutyna (U0002 → 404), więc wynik ma zawsze jeden wiersz.
        var rows = await db.QueryAsync(
            """
            SELECT id, employee_id, employee_name, company_short_name, number, kind, contract_type, signed_at,
                   start_date, end_date, terminated_at, status, retention_until, row_version
            FROM employment_app.employment_get($1)
            """,
            [Db.Param(id)],
            reader => new GetEmployment.Response(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetFieldValue<DateOnly>(7),
                reader.GetFieldValue<DateOnly>(8),
                reader.IsDBNull(9) ? null : reader.GetFieldValue<DateOnly>(9),
                reader.IsDBNull(10) ? null : reader.GetFieldValue<DateOnly>(10),
                reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetFieldValue<DateOnly>(12),
                reader.GetInt64(13),
                []
            ),
            cancellationToken
        );
        var terms = await db.QueryAsync(
            """
            SELECT id, valid_from, valid_to, position_id, position_name, fte_numerator, fte_denominator, pay_basis,
                   base_amount, zus_title_code, zus_pension_code, zus_disability_code, insurances, cost_kind
            FROM employment_app.terms_list($1)
            """,
            [Db.Param(id)],
            reader => new GetEmployment.Terms(
                reader.GetGuid(0),
                reader.GetFieldValue<DateOnly>(1),
                reader.IsDBNull(2) ? null : reader.GetFieldValue<DateOnly>(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetInt32(6),
                reader.GetString(7),
                reader.GetDecimal(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.GetFieldValue<string[]>(12),
                reader.GetString(13)
            ),
            cancellationToken
        );
        return rows[0] with { TermsHistory = terms };
    }
}
