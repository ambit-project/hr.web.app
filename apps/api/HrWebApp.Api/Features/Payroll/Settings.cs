using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HrWebApp.Api.Features.Payroll;

public static class ListPayrollSettings
{
    /// <summary>
    /// Okresy ustawień płacowych spółki od najnowszego: stopa składki wypadkowej jako <b>ułamek</b> (0,0167 = 1,67%)
    /// i czy spółka wypłaca zasiłki (powyżej 20 ubezpieczonych). Pusty <c>ValidTo</c> — okres otwarty. Bez okresu
    /// lista liczy wypadkową ze stopy domyślnej (parametr <c>zus.wypadkowa.domyslna</c>), a zasiłek — jako ZUS.
    /// </summary>
    public sealed record Item(
        Guid Id,
        DateOnly ValidFrom,
        DateOnly? ValidTo,
        decimal AccidentRate,
        bool PaysSicknessAllowance
    );

    public static async Task<Ok<List<Item>>> Handle(
        Guid id,
        PayrollSettingsHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.ListAsync(id, cancellationToken));
}

public static class AddPayrollSettings
{
    /// <summary>
    /// Nowy okres od 1. dnia miesiąca (<c>validFrom</c>: <c>invalid</c>, <c>beforeCurrent</c>); ten sam dzień co
    /// ostatni okres — jego zmiana, późniejszy — poprzedni kończy się dzień wcześniej. <c>AccidentRate</c> jako ułamek,
    /// 0 &lt; x &lt; 0,2, do czterech miejsc (<c>invalid</c>). Okres od miesiąca zamkniętej listy płac → 409
    /// <c>payrollClosed</c>; naliczone listy w okresie stają się nieaktualne.
    /// </summary>
    public sealed record Request(
        DateOnly? ValidFrom,
        decimal? AccidentRate,
        bool? PaysSicknessAllowance
    );

    public sealed record Response(Guid Id);

    public static async Task<Ok<Response>> Handle(
        Guid id,
        Request request,
        PayrollSettingsHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(new Response(await handler.AddAsync(id, request, cancellationToken)));
}

public sealed class PayrollSettingsHandler(DbSession db)
{
    public async Task<List<ListPayrollSettings.Item>> ListAsync(
        Guid companyId,
        CancellationToken cancellationToken
    ) =>
        await db.QueryAsync(
            """
            SELECT id, valid_from, valid_to, accident_rate, pays_sickness_allowance
            FROM payroll_app.company_settings_list($1)
            """,
            [Db.Param(companyId)],
            reader => new ListPayrollSettings.Item(
                reader.GetGuid(0),
                reader.GetFieldValue<DateOnly>(1),
                reader.IsDBNull(2) ? null : reader.GetFieldValue<DateOnly>(2),
                reader.GetDecimal(3),
                reader.GetBoolean(4)
            ),
            cancellationToken
        );

    public async Task<Guid> AddAsync(
        Guid companyId,
        AddPayrollSettings.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<Guid>(
            "CALL payroll_app.company_settings_set($1, $2, $3, $4, NULL)",
            [
                Db.Param(companyId),
                Db.Param(request.ValidFrom),
                Db.Param(request.AccidentRate),
                Db.Param(request.PaysSicknessAllowance),
            ],
            cancellationToken
        );
}
