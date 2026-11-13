using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HrWebApp.Api.Features.TimeAbsence;

public static class GetLeaveEntitlement
{
    /// <summary>
    /// Limit urlopu umowy o pracę na rok, w godzinach. <c>ProposedHours</c> — propozycja z KP dla <c>baseDays</c>
    /// (20 albo 26) i <c>firstJob</c> z zapytania, inaczej zapisanych (domyślnie 20). <c>EntitledHours</c>
    /// i <c>CarriedOverHours</c> — wyliczone albo poprawione przez Kadry (<c>*Overridden</c>); bez zapisanego limitu
    /// limit = 0. Saldo = limit + zaległy − wykorzystany (UW, UZ); dni = godziny ÷ 8. <c>CarriedOverWarning</c> — po
    /// 30.09 został zaległy urlop (KP art. 168). Inna umowa niż o pracę → 409 <c>kindNotAllowed</c>.
    /// </summary>
    public sealed record Response(
        bool Saved,
        int? BaseDays,
        bool? FirstJob,
        decimal ProposedHours,
        decimal EntitledHours,
        bool EntitledOverridden,
        decimal CarriedOverHours,
        bool CarriedOverOverridden,
        string? OverrideReason,
        decimal UsedHours,
        decimal BalanceHours,
        decimal BalanceDays,
        bool CarriedOverWarning,
        long? RowVersion
    );

    public static async Task<Ok<Response>> Handle(
        Guid id,
        int year,
        int? baseDays,
        bool? firstJob,
        LeaveHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.GetAsync(id, year, baseDays, firstJob, cancellationToken));
}

public static class SetLeaveEntitlement
{
    /// <summary>
    /// <c>BaseDays</c>: 20 albo 26. Puste <c>EntitledHours</c> / <c>CarriedOverHours</c> = wyliczane; podane
    /// wymagają <c>OverrideReason</c>. Nowy limit — <c>RowVersion</c> pusty, zmiana — wersja wiersza.
    /// </summary>
    public sealed record Request(
        int? BaseDays,
        bool? FirstJob,
        decimal? EntitledHours,
        decimal? CarriedOverHours,
        string? OverrideReason,
        long? RowVersion
    );

    public static async Task<NoContent> Handle(
        Guid id,
        int year,
        Request request,
        LeaveHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.SetAsync(id, year, request, cancellationToken);
        return TypedResults.NoContent();
    }
}

public static class GetMyLeaveBalance
{
    /// <summary>Saldo urlopu każdej umowy o pracę kartoteki w roku. Pracownik czyta tylko swoją kartotekę.</summary>
    public sealed record Item(
        Guid EmploymentId,
        string EmploymentNumber,
        decimal EntitledHours,
        decimal CarriedOverHours,
        decimal UsedHours,
        decimal BalanceHours,
        decimal BalanceDays
    );

    public static async Task<Ok<List<Item>>> Handle(
        Guid employeeId,
        int year,
        LeaveHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.MyBalanceAsync(employeeId, year, cancellationToken));
}

public static class GetEmploymentNorm
{
    /// <summary>
    /// Wymiar czasu pracy umowy o pracę w miesiącu: <c>CalendarHours</c> — pełny etat wg kalendarza spółki,
    /// <c>NormHours</c> — w okresie umowy × etat. Pusty <c>month</c> — wszystkie miesiące. Rok bez kalendarza → 409
    /// <c>calendarMissing</c>.
    /// </summary>
    public sealed record Month(
        int Number,
        decimal CalendarHours,
        int WorkDays,
        decimal NormHours,
        bool MissingDesignated
    );

    public static async Task<Ok<List<Month>>> Handle(
        Guid id,
        int year,
        int? month,
        LeaveHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.NormAsync(id, year, month, cancellationToken));
}

public sealed class LeaveHandler(DbSession db)
{
    public async Task<GetLeaveEntitlement.Response> GetAsync(
        Guid employmentId,
        int year,
        int? baseDays,
        bool? firstJob,
        CancellationToken cancellationToken
    ) =>
        (
            await db.QueryAsync(
                """
                SELECT saved, base_days, first_job, proposed_hours, entitled_hours, entitled_overridden,
                       carried_over_hours, carried_over_overridden, override_reason, used_hours, balance_hours,
                       balance_days, carried_over_warning, row_version
                FROM time_absence_app.leave_entitlement_get($1, $2, $3, $4)
                """,
                [Db.Param(employmentId), Db.Param(year), Db.Param(baseDays), Db.Param(firstJob)],
                reader => new GetLeaveEntitlement.Response(
                    reader.GetBoolean(0),
                    reader.IsDBNull(1) ? null : reader.GetInt32(1),
                    reader.IsDBNull(2) ? null : reader.GetBoolean(2),
                    reader.GetDecimal(3),
                    reader.GetDecimal(4),
                    reader.GetBoolean(5),
                    reader.GetDecimal(6),
                    reader.GetBoolean(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8),
                    reader.GetDecimal(9),
                    reader.GetDecimal(10),
                    reader.GetDecimal(11),
                    reader.GetBoolean(12),
                    reader.IsDBNull(13) ? null : reader.GetInt64(13)
                ),
                cancellationToken
            )
        ).Single();

    public async Task SetAsync(
        Guid employmentId,
        int year,
        SetLeaveEntitlement.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<object>(
            "CALL time_absence_app.leave_entitlement_set($1, $2, $3, $4, $5, $6, $7, $8)",
            [
                Db.Param(employmentId),
                Db.Param(year),
                Db.Param(request.BaseDays),
                Db.Param(request.FirstJob),
                Db.Param(request.EntitledHours),
                Db.Param(request.CarriedOverHours),
                Db.Param(request.OverrideReason),
                Db.Param(request.RowVersion),
            ],
            cancellationToken
        );

    public async Task<List<GetMyLeaveBalance.Item>> MyBalanceAsync(
        Guid employeeId,
        int year,
        CancellationToken cancellationToken
    ) =>
        await db.QueryAsync(
            """
            SELECT employment_id, employment_number, entitled_hours, carried_over_hours, used_hours, balance_hours,
                   balance_days
            FROM time_absence_app.my_leave_balance($1, $2)
            """,
            [Db.Param(employeeId), Db.Param(year)],
            reader => new GetMyLeaveBalance.Item(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetDecimal(2),
                reader.GetDecimal(3),
                reader.GetDecimal(4),
                reader.GetDecimal(5),
                reader.GetDecimal(6)
            ),
            cancellationToken
        );

    public async Task<List<GetEmploymentNorm.Month>> NormAsync(
        Guid employmentId,
        int year,
        int? month,
        CancellationToken cancellationToken
    ) =>
        await db.QueryAsync(
            """
            SELECT month, calendar_hours, work_days, norm_hours, missing_designated
            FROM time_absence_app.employment_norm($1, $2, $3)
            """,
            [Db.Param(employmentId), Db.Param(year), Db.Param(month)],
            reader => new GetEmploymentNorm.Month(
                reader.GetInt32(0),
                reader.GetDecimal(1),
                reader.GetInt32(2),
                reader.GetDecimal(3),
                reader.GetBoolean(4)
            ),
            cancellationToken
        );
}
