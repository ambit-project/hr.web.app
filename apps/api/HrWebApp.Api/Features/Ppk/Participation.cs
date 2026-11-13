using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HrWebApp.Api.Features.Ppk;

public static class ListPpkCandidates
{
    /// <summary>
    /// Lista „do zapisu” spółki na dzień <c>on</c> (pusty — dziś): osoby zatrudnione 18–69 lat bez uczestnictwa
    /// i bez rezygnacji. <c>ReachedOn</c> — dzień, w którym upłynęły (upłyną) 3 miesiące zatrudnienia z 12 miesięcy
    /// wstecz; <c>Deadline</c> — 10. dzień następnego miesiąca. Status: <c>waiting</c>, <c>toEnroll</c>,
    /// <c>overdue</c> (termin minął — umowa z mocy prawa), <c>requestOnly</c> (55–69 bez wniosku).
    /// </summary>
    public sealed record Item(
        Guid EmployeeId,
        string EmployeeNumber,
        string FirstName,
        string LastName,
        DateOnly BirthDate,
        int Age,
        DateOnly ReachedOn,
        DateOnly Deadline,
        string Status,
        bool EnrollmentRequest
    );

    public static async Task<Ok<List<Item>>> Handle(
        Guid id,
        DateOnly? on,
        PpkParticipationHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.CandidatesAsync(id, on, cancellationToken));
}

public static class GetEmployeePpk
{
    /// <summary>
    /// PPK kartoteki dziś. <c>Status</c>: <c>active</c>, <c>resigned</c>, <c>notEnrolled</c>. Stawki w procentach
    /// obowiązujące dziś — zera, gdy status inny niż <c>active</c> (pracodawcy: podstawowa z parametru prawnego,
    /// dodatkowa z ustawień spółki). <c>RatePeriods</c> i <c>Declarations</c> od najnowszych. Deklaracja
    /// <c>Kind</c>: <c>resignation</c>, <c>restartRequest</c>, <c>enrollmentRequest</c>, <c>reducedRate</c>,
    /// <c>additionalRate</c>, <c>autoRestart</c> (wznowienie z cyklu co 4 lata, <c>CycleYear</c>).
    /// <c>RowVersion</c> — wersja wiersza uczestnictwa (pusta bez uczestnictwa), do jego usunięcia.
    /// </summary>
    public sealed record Response(
        Guid EmployeeId,
        bool Enrolled,
        DateOnly? EnrolledAt,
        string? InstitutionMemberNumber,
        string Status,
        decimal EmployeeBasicRate,
        decimal EmployeeAdditionalRate,
        decimal EmployerBasicRate,
        decimal EmployerAdditionalRate,
        bool EmployedPerson,
        int Age,
        long? RowVersion,
        List<RatePeriod> RatePeriods,
        List<Declaration> Declarations
    );

    public sealed record RatePeriod(
        DateOnly ValidFrom,
        DateOnly? ValidTo,
        decimal EmployeeBasicRate,
        decimal EmployeeAdditionalRate
    );

    public sealed record Declaration(
        Guid Id,
        string Kind,
        DateOnly SubmittedAt,
        DateOnly EffectiveFrom,
        decimal? Rate,
        int? CycleYear,
        long RowVersion
    );

    public static async Task<Ok<Response>> Handle(
        Guid id,
        PpkParticipationHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.GetAsync(id, cancellationToken));
}

public static class GetMyPpk
{
    /// <summary>PPK kartoteki (jak <c>GetEmployeePpk</c>). Pracownik czyta tylko swoją kartotekę.</summary>
    public static async Task<Ok<GetEmployeePpk.Response>> Handle(
        Guid employeeId,
        PpkParticipationHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.GetAsync(employeeId, cancellationToken));
}

public static class EnrollInPpk
{
    /// <summary>
    /// Zapis — data zawarcia umowy o prowadzenie PPK. <c>enrolledAt</c>: <c>notEmployed</c>, <c>tooEarly</c> (mniej niż
    /// 14 dni zatrudnienia), <c>tooYoung</c>, <c>tooOld</c> (70+), <c>requestMissing</c> (55–69 bez wniosku).
    /// Stawki w procentach: podstawowa 0,5–2, dodatkowa 0–2 (pusta = 0). Drugi zapis → 409 <c>alreadyEnrolled</c>.
    /// </summary>
    public sealed record Request(
        DateOnly? EnrolledAt,
        string? InstitutionMemberNumber,
        decimal? EmployeeBasicRate,
        decimal? EmployeeAdditionalRate
    );

    public sealed record Response(Guid Id);

    public static async Task<Ok<Response>> Handle(
        Guid id,
        Request request,
        PpkParticipationHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(new Response(await handler.EnrollAsync(id, request, cancellationToken)));
}

public static class DeletePpkParticipation
{
    /// <summary>
    /// Usunięcie uczestnictwa wpisanego przez pomyłkę, z wersją wiersza: znikają uczestnictwo i okresy stawek,
    /// deklaracje zostają. Nieaktualna wersja → 409.
    /// </summary>
    public static async Task<NoContent> Handle(
        Guid id,
        long rowVersion,
        PpkParticipationHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.DeleteParticipationAsync(id, rowVersion, cancellationToken);
        return TypedResults.NoContent();
    }
}

public static class CreatePpkDeclaration
{
    /// <summary>
    /// <c>Kind</c>: <c>resignation</c> (brak wpłat od miesiąca złożenia), <c>restartRequest</c>,
    /// <c>reducedRate</c>, <c>additionalRate</c> (od następnego miesiąca), <c>enrollmentRequest</c> (wniosek osoby
    /// 55–69). <c>Rate</c> w procentach tylko przy <c>reducedRate</c> (0,5–2) i <c>additionalRate</c> (0–2).
    /// 409: <c>alreadyResigned</c>, <c>notResigned</c>, <c>notEnrolled</c>.
    /// </summary>
    public sealed record Request(string? Kind, DateOnly? SubmittedAt, decimal? Rate);

    public sealed record Response(Guid Id);

    public static async Task<Ok<Response>> Handle(
        Guid id,
        Request request,
        PpkParticipationHandler handler,
        CancellationToken cancellationToken
    ) =>
        TypedResults.Ok(
            new Response(await handler.CreateDeclarationAsync(id, request, cancellationToken))
        );
}

public static class DeletePpkDeclaration
{
    /// <summary>Usunięcie deklaracji wpisanej pomyłkowo, z wersją wiersza.</summary>
    public static async Task<NoContent> Handle(
        Guid id,
        long rowVersion,
        PpkParticipationHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.DeleteDeclarationAsync(id, rowVersion, cancellationToken);
        return TypedResults.NoContent();
    }
}

public sealed class PpkParticipationHandler(DbSession db)
{
    public async Task<List<ListPpkCandidates.Item>> CandidatesAsync(
        Guid companyId,
        DateOnly? on,
        CancellationToken cancellationToken
    ) =>
        await db.QueryAsync(
            """
            SELECT employee_id, employee_number, first_name, last_name, birth_date, age, reached_on, deadline,
                   status, enrollment_request
            FROM ppk_app.enrollment_candidates($1, $2)
            """,
            [Db.Param(companyId), Db.Param(on)],
            reader => new ListPpkCandidates.Item(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetFieldValue<DateOnly>(4),
                reader.GetInt32(5),
                reader.GetFieldValue<DateOnly>(6),
                reader.GetFieldValue<DateOnly>(7),
                reader.GetString(8),
                reader.GetBoolean(9)
            ),
            cancellationToken
        );

    public async Task<GetEmployeePpk.Response> GetAsync(
        Guid employeeId,
        CancellationToken cancellationToken
    )
    {
        var periods = await db.QueryAsync(
            """
            SELECT valid_from, valid_to, employee_basic_rate, employee_additional_rate
            FROM ppk_app.rate_period_list($1)
            """,
            [Db.Param(employeeId)],
            reader => new GetEmployeePpk.RatePeriod(
                reader.GetFieldValue<DateOnly>(0),
                reader.IsDBNull(1) ? null : reader.GetFieldValue<DateOnly>(1),
                reader.GetDecimal(2),
                reader.GetDecimal(3)
            ),
            cancellationToken
        );
        var declarations = await db.QueryAsync(
            """
            SELECT id, kind, submitted_at, effective_from, rate, cycle_year, row_version
            FROM ppk_app.declaration_list($1)
            """,
            [Db.Param(employeeId)],
            reader => new GetEmployeePpk.Declaration(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetFieldValue<DateOnly>(2),
                reader.GetFieldValue<DateOnly>(3),
                reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5),
                reader.GetInt64(6)
            ),
            cancellationToken
        );
        return (
            await db.QueryAsync(
                """
                SELECT employee_id, enrolled, enrolled_at, institution_member_number, status, employee_basic_rate,
                       employee_additional_rate, employer_basic_rate, employer_additional_rate, employed_person, age,
                       row_version
                FROM ppk_app.participation_get($1)
                """,
                [Db.Param(employeeId)],
                reader => new GetEmployeePpk.Response(
                    reader.GetGuid(0),
                    reader.GetBoolean(1),
                    reader.IsDBNull(2) ? null : reader.GetFieldValue<DateOnly>(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetString(4),
                    reader.GetDecimal(5),
                    reader.GetDecimal(6),
                    reader.GetDecimal(7),
                    reader.GetDecimal(8),
                    reader.GetBoolean(9),
                    reader.GetInt32(10),
                    reader.IsDBNull(11) ? null : reader.GetInt64(11),
                    periods,
                    declarations
                ),
                cancellationToken
            )
        ).Single();
    }

    public async Task<Guid> EnrollAsync(
        Guid employeeId,
        EnrollInPpk.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<Guid>(
            "CALL ppk_app.participation_create($1, $2, $3, $4, $5, NULL)",
            [
                Db.Param(employeeId),
                Db.Param(request.EnrolledAt),
                Db.Param(request.InstitutionMemberNumber),
                Db.Param(request.EmployeeBasicRate),
                Db.Param(request.EmployeeAdditionalRate),
            ],
            cancellationToken
        );

    public async Task DeleteParticipationAsync(
        Guid employeeId,
        long rowVersion,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<object>(
            "CALL ppk_app.participation_delete($1, $2)",
            [Db.Param(employeeId), Db.Param(rowVersion)],
            cancellationToken
        );

    public async Task<Guid> CreateDeclarationAsync(
        Guid employeeId,
        CreatePpkDeclaration.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<Guid>(
            "CALL ppk_app.declaration_create($1, $2, $3, $4, NULL)",
            [
                Db.Param(employeeId),
                Db.Param(request.Kind),
                Db.Param(request.SubmittedAt),
                Db.Param(request.Rate),
            ],
            cancellationToken
        );

    public async Task DeleteDeclarationAsync(
        Guid id,
        long rowVersion,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<object>(
            "CALL ppk_app.declaration_delete($1, $2)",
            [Db.Param(id), Db.Param(rowVersion)],
            cancellationToken
        );
}
