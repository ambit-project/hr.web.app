using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HrWebApp.Api.Features.TimeAbsence;

public static class ListOvertime
{
    /// <summary>Rodzaj dodatku <c>Rate</c>: 50 albo 100 (KP art. 151¹ § 1).</summary>
    public sealed record Entry(Guid Id, DateOnly Date, decimal Hours, int Rate, long RowVersion);

    /// <summary>
    /// Nadgodziny umowy o pracę w roku z sumą. <c>AboveLimit</c> — ostrzeżenie po przekroczeniu <c>LimitHours</c>
    /// (150 h, KP art. 151 § 3; nie blokuje zapisu).
    /// </summary>
    public sealed record Response(
        List<Entry> Entries,
        decimal TotalHours,
        decimal Hours50,
        decimal Hours100,
        decimal LimitHours,
        bool AboveLimit
    );

    public static async Task<Ok<Response>> Handle(
        Guid id,
        int year,
        WorkTimeHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.ListOvertimeAsync(id, year, cancellationToken));
}

public static class AddOvertime
{
    /// <summary>Godziny 0,5–16; <c>Rate</c> 50 albo 100. Tylko umowa o pracę (inaczej 409 <c>kindNotAllowed</c>).</summary>
    public sealed record Request(DateOnly? Date, decimal? Hours, int? Rate);

    public sealed record Response(Guid Id);

    public static async Task<Ok<Response>> Handle(
        Guid id,
        Request request,
        WorkTimeHandler handler,
        CancellationToken cancellationToken
    ) =>
        TypedResults.Ok(
            new Response(await handler.AddOvertimeAsync(id, request, cancellationToken))
        );
}

public static class DeleteOvertime
{
    public static async Task<NoContent> Handle(
        Guid id,
        long rowVersion,
        WorkTimeHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.DeleteOvertimeAsync(id, rowVersion, cancellationToken);
        return TypedResults.NoContent();
    }
}

public static class ListMandateHours
{
    /// <summary>
    /// Godziny zlecenia nachodzące na rok. Sposób potwierdzenia: <c>written</c>, <c>electronic</c>,
    /// <c>documentary</c>. <c>Confirmed</c> = false — lista płac ich nie weźmie (ostrzeżenie).
    /// </summary>
    public sealed record Item(
        Guid Id,
        DateOnly DateFrom,
        DateOnly DateTo,
        decimal Hours,
        DateOnly? ConfirmedAt,
        string? ConfirmationForm,
        bool Confirmed,
        long RowVersion
    );

    public static async Task<Ok<List<Item>>> Handle(
        Guid id,
        int year,
        WorkTimeHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.ListMandateHoursAsync(id, year, cancellationToken));
}

public static class AddMandateHours
{
    /// <summary>
    /// Okres bez nakładania na inne wpisy umowy. Potwierdzenie: data i sposób (<c>written</c>, <c>electronic</c>,
    /// <c>documentary</c>) albo oba puste. Tylko zlecenie (inaczej 409 <c>kindNotAllowed</c>).
    /// </summary>
    public sealed record Request(
        DateOnly? DateFrom,
        DateOnly? DateTo,
        decimal? Hours,
        DateOnly? ConfirmedAt,
        string? ConfirmationForm
    );

    public sealed record Response(Guid Id);

    public static async Task<Ok<Response>> Handle(
        Guid id,
        Request request,
        WorkTimeHandler handler,
        CancellationToken cancellationToken
    ) =>
        TypedResults.Ok(
            new Response(await handler.AddMandateHoursAsync(id, request, cancellationToken))
        );
}

public static class UpdateMandateHours
{
    public sealed record Request(
        long RowVersion,
        DateOnly? DateFrom,
        DateOnly? DateTo,
        decimal? Hours,
        DateOnly? ConfirmedAt,
        string? ConfirmationForm
    );

    public static async Task<NoContent> Handle(
        Guid id,
        Request request,
        WorkTimeHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.UpdateMandateHoursAsync(id, request, cancellationToken);
        return TypedResults.NoContent();
    }
}

public static class DeleteMandateHours
{
    public static async Task<NoContent> Handle(
        Guid id,
        long rowVersion,
        WorkTimeHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.DeleteMandateHoursAsync(id, rowVersion, cancellationToken);
        return TypedResults.NoContent();
    }
}

public sealed class WorkTimeHandler(DbSession db)
{
    public async Task<ListOvertime.Response> ListOvertimeAsync(
        Guid employmentId,
        int year,
        CancellationToken cancellationToken
    )
    {
        var entries = await db.QueryAsync(
            "SELECT id, date, hours, rate, row_version FROM time_absence_app.overtime_list($1, $2)",
            [Db.Param(employmentId), Db.Param(year)],
            reader => new ListOvertime.Entry(
                reader.GetGuid(0),
                reader.GetFieldValue<DateOnly>(1),
                reader.GetDecimal(2),
                reader.GetInt32(3),
                reader.GetInt64(4)
            ),
            cancellationToken
        );
        var summary = await db.QueryAsync(
            """
            SELECT total_hours, hours50, hours100, limit_hours, above_limit
            FROM time_absence_app.overtime_summary($1, $2)
            """,
            [Db.Param(employmentId), Db.Param(year)],
            reader => new ListOvertime.Response(
                entries,
                reader.GetDecimal(0),
                reader.GetDecimal(1),
                reader.GetDecimal(2),
                reader.GetDecimal(3),
                reader.GetBoolean(4)
            ),
            cancellationToken
        );
        return summary.Single();
    }

    public async Task<Guid> AddOvertimeAsync(
        Guid employmentId,
        AddOvertime.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<Guid>(
            "CALL time_absence_app.overtime_add($1, $2, $3, $4, NULL)",
            [
                Db.Param(employmentId),
                Db.Param(request.Date),
                Db.Param(request.Hours),
                Db.Param(request.Rate),
            ],
            cancellationToken
        );

    public async Task DeleteOvertimeAsync(
        Guid id,
        long rowVersion,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<object>(
            "CALL time_absence_app.overtime_delete($1, $2)",
            [Db.Param(id), Db.Param(rowVersion)],
            cancellationToken
        );

    public async Task<List<ListMandateHours.Item>> ListMandateHoursAsync(
        Guid employmentId,
        int year,
        CancellationToken cancellationToken
    ) =>
        await db.QueryAsync(
            """
            SELECT id, date_from, date_to, hours, confirmed_at, confirmation_form, confirmed, row_version
            FROM time_absence_app.mandate_hours_list($1, $2)
            """,
            [Db.Param(employmentId), Db.Param(year)],
            reader => new ListMandateHours.Item(
                reader.GetGuid(0),
                reader.GetFieldValue<DateOnly>(1),
                reader.GetFieldValue<DateOnly>(2),
                reader.GetDecimal(3),
                reader.IsDBNull(4) ? null : reader.GetFieldValue<DateOnly>(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetBoolean(6),
                reader.GetInt64(7)
            ),
            cancellationToken
        );

    public async Task<Guid> AddMandateHoursAsync(
        Guid employmentId,
        AddMandateHours.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<Guid>(
            "CALL time_absence_app.mandate_hours_add($1, $2, $3, $4, $5, $6, NULL)",
            [
                Db.Param(employmentId),
                Db.Param(request.DateFrom),
                Db.Param(request.DateTo),
                Db.Param(request.Hours),
                Db.Param(request.ConfirmedAt),
                Db.Param(request.ConfirmationForm),
            ],
            cancellationToken
        );

    public async Task UpdateMandateHoursAsync(
        Guid id,
        UpdateMandateHours.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<object>(
            "CALL time_absence_app.mandate_hours_update($1, $2, $3, $4, $5, $6, $7)",
            [
                Db.Param(id),
                Db.Param(request.RowVersion),
                Db.Param(request.DateFrom),
                Db.Param(request.DateTo),
                Db.Param(request.Hours),
                Db.Param(request.ConfirmedAt),
                Db.Param(request.ConfirmationForm),
            ],
            cancellationToken
        );

    public async Task DeleteMandateHoursAsync(
        Guid id,
        long rowVersion,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<object>(
            "CALL time_absence_app.mandate_hours_delete($1, $2)",
            [Db.Param(id), Db.Param(rowVersion)],
            cancellationToken
        );
}
