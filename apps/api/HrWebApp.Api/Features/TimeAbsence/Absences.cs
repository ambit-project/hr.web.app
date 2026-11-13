using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HrWebApp.Api.Features.TimeAbsence;

public static class ListAbsenceTypes
{
    /// <summary>
    /// Kod: <c>UW</c>, <c>UZ</c>, <c>UO</c>, <c>OD</c>, <c>SW</c>, <c>UOP</c>, <c>CH</c>, <c>CHW</c>, <c>CHC</c>,
    /// <c>UB</c>, <c>NN</c>. Płatność (wycenia lista płac): <c>leavePay</c>, <c>salary</c>, <c>salaryHalf</c>,
    /// <c>sickness80</c>, <c>sickness100</c>, <c>unpaid</c>. Limit: <c>none</c>, <c>leave</c> (limit urlopu; UZ
    /// dodatkowo <c>LimitDays</c> w roku), <c>yearDays</c> (dni w roku), <c>event</c> (dni na zdarzenie).
    /// <c>Sickness</c> — choroba (numer e-ZLA, dozwolona na zleceniu z chorobowym).
    /// </summary>
    public sealed record Item(
        string Code,
        string Name,
        string Payment,
        string LimitKind,
        int? LimitDays,
        bool Sickness,
        string? LegalBasis
    );

    public static async Task<Ok<List<Item>>> Handle(
        AbsencesHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.ListTypesAsync(cancellationToken));
}

public static class ListAbsences
{
    /// <summary>
    /// Nieobecność nachodząca na rok (pusty rok — wszystkie). Dni i godziny liczone przy odczycie: dni kalendarzowe,
    /// dni robocze kalendarza spółki i godziny pracy × etat (zlecenie — 0).
    /// </summary>
    public sealed record Item(
        Guid Id,
        string AbsenceTypeId,
        string AbsenceTypeName,
        DateOnly DateFrom,
        DateOnly DateTo,
        int CalendarDays,
        int WorkDays,
        decimal WorkHours,
        string? CertificateNumber,
        string? Note,
        long RowVersion
    );

    public static async Task<Ok<List<Item>>> Handle(
        Guid id,
        int? year,
        AbsencesHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.ListAsync(id, year, cancellationToken));
}

public static class CreateAbsence
{
    /// <summary>
    /// <c>AbsenceTypeId</c> = kod rodzaju (np. <c>UW</c>). Choroba wymaga numeru e-ZLA (do 20 znaków). Limity:
    /// 400 <c>limitExceeded</c> przy <c>dateTo</c> z <c>remainingDays</c>, <c>leaveLimitExceeded</c>
    /// z <c>remainingHours</c>; umowa o pracę bez kalendarza spółki → 409 <c>calendarMissing</c>.
    /// </summary>
    public sealed record Request(
        string? AbsenceTypeId,
        DateOnly? DateFrom,
        DateOnly? DateTo,
        string? CertificateNumber,
        string? Note
    );

    public sealed record Response(Guid Id);

    public static async Task<Ok<Response>> Handle(
        Guid id,
        Request request,
        AbsencesHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(new Response(await handler.CreateAsync(id, request, cancellationToken)));
}

public static class UpdateAbsence
{
    public sealed record Request(
        long RowVersion,
        string? AbsenceTypeId,
        DateOnly? DateFrom,
        DateOnly? DateTo,
        string? CertificateNumber,
        string? Note
    );

    public static async Task<NoContent> Handle(
        Guid id,
        Request request,
        AbsencesHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.UpdateAsync(id, request, cancellationToken);
        return TypedResults.NoContent();
    }
}

public static class DeleteAbsence
{
    public static async Task<NoContent> Handle(
        Guid id,
        long rowVersion,
        AbsencesHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.DeleteAsync(id, rowVersion, cancellationToken);
        return TypedResults.NoContent();
    }
}

public static class ListMyAbsences
{
    /// <summary>Nieobecności kartoteki w roku ze wszystkich umów. Pracownik czyta tylko swoją kartotekę.</summary>
    public sealed record Item(
        Guid Id,
        Guid EmploymentId,
        string EmploymentNumber,
        string AbsenceTypeId,
        string AbsenceTypeName,
        DateOnly DateFrom,
        DateOnly DateTo,
        int CalendarDays,
        decimal WorkHours
    );

    public static async Task<Ok<List<Item>>> Handle(
        Guid employeeId,
        int year,
        AbsencesHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.ListMineAsync(employeeId, year, cancellationToken));
}

public sealed class AbsencesHandler(DbSession db)
{
    public async Task<List<ListAbsenceTypes.Item>> ListTypesAsync(
        CancellationToken cancellationToken
    ) =>
        await db.QueryAsync(
            """
            SELECT code, name, payment, limit_kind, limit_days, sickness, legal_basis
            FROM time_absence_app.absence_type_list()
            """,
            [],
            reader => new ListAbsenceTypes.Item(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.GetBoolean(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)
            ),
            cancellationToken
        );

    public async Task<List<ListAbsences.Item>> ListAsync(
        Guid employmentId,
        int? year,
        CancellationToken cancellationToken
    ) =>
        await db.QueryAsync(
            """
            SELECT id, absence_type_id, absence_type_name, date_from, date_to, calendar_days, work_days, work_hours,
                   certificate_number, note, row_version
            FROM time_absence_app.absence_list($1, $2)
            """,
            [Db.Param(employmentId), Db.Param(year)],
            reader => new ListAbsences.Item(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetFieldValue<DateOnly>(3),
                reader.GetFieldValue<DateOnly>(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetDecimal(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.GetInt64(10)
            ),
            cancellationToken
        );

    public async Task<Guid> CreateAsync(
        Guid employmentId,
        CreateAbsence.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<Guid>(
            "CALL time_absence_app.absence_create($1, $2, $3, $4, $5, $6, NULL)",
            [
                Db.Param(employmentId),
                Db.Param(request.AbsenceTypeId),
                Db.Param(request.DateFrom),
                Db.Param(request.DateTo),
                Db.Param(request.CertificateNumber),
                Db.Param(request.Note),
            ],
            cancellationToken
        );

    public async Task UpdateAsync(
        Guid id,
        UpdateAbsence.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<object>(
            "CALL time_absence_app.absence_update($1, $2, $3, $4, $5, $6, $7)",
            [
                Db.Param(id),
                Db.Param(request.RowVersion),
                Db.Param(request.AbsenceTypeId),
                Db.Param(request.DateFrom),
                Db.Param(request.DateTo),
                Db.Param(request.CertificateNumber),
                Db.Param(request.Note),
            ],
            cancellationToken
        );

    public async Task DeleteAsync(Guid id, long rowVersion, CancellationToken cancellationToken) =>
        await db.ScalarAsync<object>(
            "CALL time_absence_app.absence_delete($1, $2)",
            [Db.Param(id), Db.Param(rowVersion)],
            cancellationToken
        );

    public async Task<List<ListMyAbsences.Item>> ListMineAsync(
        Guid employeeId,
        int year,
        CancellationToken cancellationToken
    ) =>
        await db.QueryAsync(
            """
            SELECT id, employment_id, employment_number, absence_type_id, absence_type_name, date_from, date_to,
                   calendar_days, work_hours
            FROM time_absence_app.my_absence_list($1, $2)
            """,
            [Db.Param(employeeId), Db.Param(year)],
            reader => new ListMyAbsences.Item(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetFieldValue<DateOnly>(5),
                reader.GetFieldValue<DateOnly>(6),
                reader.GetInt32(7),
                reader.GetDecimal(8)
            ),
            cancellationToken
        );
}
