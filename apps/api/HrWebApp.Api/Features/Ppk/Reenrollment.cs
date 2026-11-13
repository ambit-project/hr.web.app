using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HrWebApp.Api.Features.Ppk;

public static class GetPpkReenrollment
{
    /// <summary>
    /// Ponowny zapis co 4 lata w roku cyklu (rok spoza cyklu → 400 <c>year</c>: <c>notCycleYear</c>):
    /// <c>InformationDeadline</c> (ostatni dzień lutego), <c>RestartDate</c> (1 kwietnia), daty przekazania informacji
    /// i wykonania wznowienia. <c>Items</c> — osoby z rezygnacją; <c>Group</c>: <c>restart</c> (do 55 lat),
    /// <c>requestOnly</c> (55–69, tylko na wniosek), <c>noRestart</c> (70+). <c>ResignedAfterInformation</c> — nowa
    /// rezygnacja po informacji (bez wznowienia), <c>Restarted</c> — wznowiono w tym cyklu.
    /// </summary>
    public sealed record Response(
        int Year,
        DateOnly InformationDeadline,
        DateOnly RestartDate,
        DateOnly? InformedAt,
        DateTimeOffset? AppliedAt,
        List<Item> Items
    );

    public sealed record Item(
        Guid EmployeeId,
        string EmployeeNumber,
        string FirstName,
        string LastName,
        DateOnly BirthDate,
        int Age,
        DateOnly ResignedFrom,
        string Group,
        bool ResignedAfterInformation,
        bool Restarted
    );

    public static async Task<Ok<Response>> Handle(
        Guid id,
        int year,
        PpkReenrollmentHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.GetAsync(id, year, cancellationToken));
}

public static class MarkPpkReenrollmentInformed
{
    /// <summary>
    /// Data przekazania informacji o ponownym zapisie: w roku cyklu, przed dniem wznowienia (<c>invalid</c>), nie
    /// z przyszłości. Po wykonaniu wznowienia → 409 <c>applied</c>.
    /// </summary>
    public sealed record Request(DateOnly? InformedAt);

    public static async Task<NoContent> Handle(
        Guid id,
        int year,
        Request request,
        PpkReenrollmentHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.MarkInformedAsync(id, year, request.InformedAt, cancellationToken);
        return TypedResults.NoContent();
    }
}

public static class ApplyPpkReenrollment
{
    /// <summary>
    /// Wznowienie wpłat grupie <c>restart</c> od dnia wznowienia (bez osób z rezygnacją po informacji). Wymaga daty
    /// informacji (409 <c>notInformed</c>). Powtórzenie wznawia tylko brakujących; <c>Restarted</c> — ilu teraz.
    /// </summary>
    public sealed record Response(int Restarted);

    public static async Task<Ok<Response>> Handle(
        Guid id,
        int year,
        PpkReenrollmentHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(new Response(await handler.ApplyAsync(id, year, cancellationToken)));
}

public sealed class PpkReenrollmentHandler(DbSession db)
{
    public async Task<GetPpkReenrollment.Response> GetAsync(
        Guid companyId,
        int year,
        CancellationToken cancellationToken
    )
    {
        var items = await db.QueryAsync(
            """
            SELECT employee_id, employee_number, first_name, last_name, birth_date, age, resigned_from, group_kind,
                   resigned_after_information, restarted
            FROM ppk_app.reenrollment_list($1, $2)
            """,
            [Db.Param(companyId), Db.Param(year)],
            reader => new GetPpkReenrollment.Item(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetFieldValue<DateOnly>(4),
                reader.GetInt32(5),
                reader.GetFieldValue<DateOnly>(6),
                reader.GetString(7),
                reader.GetBoolean(8),
                reader.GetBoolean(9)
            ),
            cancellationToken
        );
        return (
            await db.QueryAsync(
                """
                SELECT year, information_deadline, restart_date, informed_at, applied_at
                FROM ppk_app.reenrollment_get($1, $2)
                """,
                [Db.Param(companyId), Db.Param(year)],
                reader => new GetPpkReenrollment.Response(
                    reader.GetInt32(0),
                    reader.GetFieldValue<DateOnly>(1),
                    reader.GetFieldValue<DateOnly>(2),
                    reader.IsDBNull(3) ? null : reader.GetFieldValue<DateOnly>(3),
                    reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4),
                    items
                ),
                cancellationToken
            )
        ).Single();
    }

    public async Task MarkInformedAsync(
        Guid companyId,
        int year,
        DateOnly? informedAt,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<object>(
            "CALL ppk_app.reenrollment_mark_informed($1, $2, $3)",
            [Db.Param(companyId), Db.Param(year), Db.Param(informedAt)],
            cancellationToken
        );

    public async Task<int> ApplyAsync(
        Guid companyId,
        int year,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<int>(
            "CALL ppk_app.reenrollment_apply($1, $2, NULL)",
            [Db.Param(companyId), Db.Param(year)],
            cancellationToken
        );
}
