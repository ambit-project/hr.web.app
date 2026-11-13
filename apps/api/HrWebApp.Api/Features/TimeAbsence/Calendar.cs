using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HrWebApp.Api.Features.TimeAbsence;

public static class ListHolidays
{
    /// <summary><c>IsSaturday</c> — święto w sobotę obniża wymiar o 8 h (KP art. 130 § 2).</summary>
    public sealed record Item(DateOnly Date, string Name, bool IsSaturday);

    public static async Task<Ok<List<Item>>> Handle(
        int year,
        CalendarHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.ListHolidaysAsync(year, cancellationToken));
}

public static class AddHoliday
{
    /// <summary>Tylko Administrator. Kalendarze już wygenerowane się nie zmieniają.</summary>
    public sealed record Request(DateOnly? Date, string? Name);

    public static async Task<NoContent> Handle(
        Request request,
        CalendarHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.AddHolidayAsync(request, cancellationToken);
        return TypedResults.NoContent();
    }
}

public static class DeleteHoliday
{
    public static async Task<NoContent> Handle(
        DateOnly date,
        CalendarHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.DeleteHolidayAsync(date, cancellationToken);
        return TypedResults.NoContent();
    }
}

public static class GenerateCalendar
{
    /// <summary>
    /// Liczba świąt w sobotę, za które trzeba wyznaczyć dzień wolny. Rok z kalendarzem → 409 <c>calendarExists</c>,
    /// rok bez świąt w słowniku → 409 <c>holidaysMissing</c>.
    /// </summary>
    public sealed record Response(int SaturdayHolidays);

    public static async Task<Ok<Response>> Handle(
        Guid id,
        int year,
        CalendarHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(new Response(await handler.GenerateAsync(id, year, cancellationToken)));
}

public static class GetCalendarYear
{
    /// <summary>
    /// Miesiąc kalendarza; pusta lista = rok bez kalendarza. <c>MissingDesignated</c> — brak dnia wolnego za święto
    /// w sobotę (wymiar o 8 h za wysoki).
    /// </summary>
    public sealed record Month(
        int Number,
        int WorkDays,
        decimal Hours,
        int SaturdayHolidays,
        int DesignatedOff,
        bool MissingDesignated
    );

    public static async Task<Ok<List<Month>>> Handle(
        Guid id,
        int year,
        CalendarHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.YearAsync(id, year, cancellationToken));
}

public static class GetCalendarMonth
{
    /// <summary>Rodzaj dnia: <c>workday</c>, <c>dayOff</c>, <c>holiday</c>, <c>designatedOff</c>.</summary>
    public sealed record Day(DateOnly Date, string Kind, decimal Hours, string? HolidayName);

    public static async Task<Ok<List<Day>>> Handle(
        Guid id,
        int year,
        int month,
        CalendarHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.MonthAsync(id, year, month, cancellationToken));
}

public static class SetCalendarDay
{
    /// <summary>
    /// Rodzaj: <c>workday</c> (godziny 0–12), <c>dayOff</c>, <c>holiday</c> (tylko święto ze słownika),
    /// <c>designatedOff</c> (pn–pt w miesiącu ze świętem w sobotę). Dzień inny niż roboczy ma 0 h.
    /// </summary>
    public sealed record Request(string? Kind, decimal? Hours);

    public static async Task<NoContent> Handle(
        Guid id,
        DateOnly date,
        Request request,
        CalendarHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.SetDayAsync(id, date, request, cancellationToken);
        return TypedResults.NoContent();
    }
}

public sealed class CalendarHandler(DbSession db)
{
    public async Task<List<ListHolidays.Item>> ListHolidaysAsync(
        int year,
        CancellationToken cancellationToken
    ) =>
        await db.QueryAsync(
            "SELECT date, name, is_saturday FROM time_absence_app.holiday_list($1)",
            [Db.Param(year)],
            reader => new ListHolidays.Item(
                reader.GetFieldValue<DateOnly>(0),
                reader.GetString(1),
                reader.GetBoolean(2)
            ),
            cancellationToken
        );

    public async Task AddHolidayAsync(
        AddHoliday.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<object>(
            "CALL time_absence_app.holiday_add($1, $2)",
            [Db.Param(request.Date), Db.Param(request.Name)],
            cancellationToken
        );

    public async Task DeleteHolidayAsync(DateOnly date, CancellationToken cancellationToken) =>
        await db.ScalarAsync<object>(
            "CALL time_absence_app.holiday_delete($1)",
            [Db.Param(date)],
            cancellationToken
        );

    public async Task<int> GenerateAsync(
        Guid companyId,
        int year,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<int>(
            "CALL time_absence_app.calendar_generate($1, $2, NULL)",
            [Db.Param(companyId), Db.Param(year)],
            cancellationToken
        );

    public async Task<List<GetCalendarYear.Month>> YearAsync(
        Guid companyId,
        int year,
        CancellationToken cancellationToken
    ) =>
        await db.QueryAsync(
            """
            SELECT month, work_days, hours, saturday_holidays, designated_off, missing_designated
            FROM time_absence_app.calendar_year($1, $2)
            """,
            [Db.Param(companyId), Db.Param(year)],
            reader => new GetCalendarYear.Month(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetDecimal(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetBoolean(5)
            ),
            cancellationToken
        );

    public async Task<List<GetCalendarMonth.Day>> MonthAsync(
        Guid companyId,
        int year,
        int month,
        CancellationToken cancellationToken
    ) =>
        await db.QueryAsync(
            "SELECT date, kind, hours, holiday_name FROM time_absence_app.calendar_month($1, $2, $3)",
            [Db.Param(companyId), Db.Param(year), Db.Param(month)],
            reader => new GetCalendarMonth.Day(
                reader.GetFieldValue<DateOnly>(0),
                reader.GetString(1),
                reader.GetDecimal(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)
            ),
            cancellationToken
        );

    public async Task SetDayAsync(
        Guid companyId,
        DateOnly date,
        SetCalendarDay.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<object>(
            "CALL time_absence_app.calendar_day_set($1, $2, $3, $4)",
            [Db.Param(companyId), Db.Param(date), Db.Param(request.Kind), Db.Param(request.Hours)],
            cancellationToken
        );
}
