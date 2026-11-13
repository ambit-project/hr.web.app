using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HrWebApp.Api.Features.Payroll;

public static class ListPayrolls
{
    /// <summary>
    /// Listy płac spółki z okresem w roku, od najnowszego okresu. <c>Kind</c>: <c>employment</c> (umowy o pracę),
    /// <c>mandate</c> (zlecenia), <c>specificWork</c> (dzieła), <c>correction</c> (korekta listy
    /// <c>CorrectsPayrollId</c> / <c>CorrectsNumber</c>). <c>Status</c>: <c>open</c>, <c>calculated</c>,
    /// <c>closed</c>; <c>IsStale</c> — naliczona lista po zmianie danych źródłowych (trzeba przeliczyć).
    /// <c>PaymentDateLate</c> — wypłata po 10. dniu następnego miesiąca (KP art. 85 § 2, ostrzeżenie). Sumy
    /// brutto, netto i do wypłaty z wypłat listy.
    /// </summary>
    public sealed record Item(
        Guid Id,
        string Number,
        string Kind,
        DateOnly PeriodFrom,
        DateOnly PeriodTo,
        DateOnly PaymentDate,
        bool PaymentDateLate,
        Guid? CorrectsPayrollId,
        string? CorrectsNumber,
        string Status,
        bool IsStale,
        int EntryCount,
        decimal Gross,
        decimal Net,
        decimal Payout,
        long RowVersion
    );

    public static async Task<Ok<List<Item>>> Handle(
        Guid id,
        int year,
        PayrollsHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.ListAsync(id, year, cancellationToken));
}

public static class CreatePayroll
{
    /// <summary>
    /// Nowa lista. <c>Kind</c>: <c>employment</c>, <c>mandate</c>, <c>specificWork</c> — okres = miesiąc z
    /// <c>Period</c> (dowolny dzień miesiąca), jedna lista rodzaju na miesiąc (<c>period</c> <c>duplicate</c>), skład
    /// z umów tego rodzaju trwających w okresie, numer <c>RRRR/MM/UP|UZ|UD</c>; <c>correction</c> —
    /// <c>CorrectsPayrollId</c> = zamknięta lista zwykła spółki (<c>notFound</c>, <c>invalid</c>), okres listy
    /// korygowanej (<c>Period</c> pomijany), skład = umowy z jej wypłatami, numer <c>&lt;numer&gt;/K&lt;n&gt;</c>.
    /// <c>PaymentDate</c> wymagana, nie przed początkiem okresu (<c>beforeStart</c>).
    /// </summary>
    public sealed record Request(
        string? Kind,
        DateOnly? Period,
        DateOnly? PaymentDate,
        Guid? CorrectsPayrollId
    );

    public sealed record Response(Guid Id);

    public static async Task<Ok<Response>> Handle(
        Guid id,
        Request request,
        PayrollsHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(new Response(await handler.CreateAsync(id, request, cancellationToken)));
}

public static class GetPayroll
{
    /// <summary>
    /// Lista z podsumowaniem. <c>Kind</c>: <c>employment</c>, <c>mandate</c>, <c>specificWork</c>,
    /// <c>correction</c>; <c>Status</c>: <c>open</c>, <c>calculated</c>, <c>closed</c>; <c>IsStale</c> —
    /// nieaktualna (zamknięcie → 409 <c>stale</c>). <c>PpkPaidOn</c> — data przekazania wpłat PPK do instytucji
    /// finansowej (od niej wpłata pracodawcy jest przychodem pracownika). Liczby: wypłaty, wypłaty z ostrzeżeniami,
    /// wypłaty z ostrzeżeniem blokującym zamknięcie (<c>BlockedCount</c> — przypadki, których system nie liczy), umowy
    /// w składzie i wyłączone. Sumy: brutto, składki społeczne pracownika, zdrowotna, PIT, PPK pracownika, netto, potrącenia,
    /// do wypłaty, koszt pracodawcy (składki i PPK pracodawcy). <c>RowVersion</c> — do zapisu listy.
    /// </summary>
    public sealed record Response(
        Guid Id,
        Guid CompanyId,
        string Number,
        string Kind,
        DateOnly PeriodFrom,
        DateOnly PeriodTo,
        DateOnly PaymentDate,
        bool PaymentDateLate,
        Guid? CorrectsPayrollId,
        string? CorrectsNumber,
        string Status,
        bool IsStale,
        DateTimeOffset? CalculatedAt,
        DateTimeOffset? ClosedAt,
        DateOnly? PpkPaidOn,
        int EntryCount,
        int WarningCount,
        int BlockedCount,
        int MemberCount,
        int ExcludedCount,
        decimal Gross,
        decimal EmployeeContributions,
        decimal Health,
        decimal Pit,
        decimal PpkEmployee,
        decimal Net,
        decimal Deductions,
        decimal Payout,
        decimal EmployerCost,
        long RowVersion
    );

    public static async Task<Ok<Response>> Handle(
        Guid id,
        PayrollsHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.GetAsync(id, cancellationToken));
}

public static class DeletePayroll
{
    /// <summary>
    /// Usunięcie listy otwartej albo naliczonej z wersją wiersza (pusta — bez sprawdzania); zamknięta → 409
    /// <c>closed</c>, nieaktualna wersja → 409 bez powodu.
    /// </summary>
    public static async Task<NoContent> Handle(
        Guid id,
        long? rowVersion,
        PayrollsHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.DeleteAsync(id, rowVersion, cancellationToken);
        return TypedResults.NoContent();
    }
}

/// <summary>Wersja wiersza listy z <c>GetPayroll</c>; pusta — bez sprawdzania.</summary>
public sealed record PayrollVersion(long? RowVersion);

public static class CalculatePayroll
{
    /// <summary>
    /// Naliczenie albo przeliczenie listy: odświeżenie składu zwykłej listy, nowe wypłaty, status
    /// <c>calculated</c>, <c>IsStale</c> = false. Zamknięta → 409 <c>closed</c>; umowa o pracę bez kalendarza spółki →
    /// 409 <c>calendarMissing</c>; brak parametru prawnego → 409 (U0006).
    /// </summary>
    public static async Task<NoContent> Handle(
        Guid id,
        PayrollVersion request,
        PayrollsHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.CalculateAsync(id, request.RowVersion, cancellationToken);
        return TypedResults.NoContent();
    }
}

public static class ClosePayroll
{
    /// <summary>
    /// Zamknięcie listy: tylko naliczona (409 <c>notCalculated</c>), aktualna (409 <c>stale</c>) i bez wypłat
    /// z ostrzeżeniem blokującym (409 <c>unsupportedCases</c> — przypadki, których system nie liczy). Po zamknięciu dane
    /// okresu są zablokowane (zmiana → 409 <c>payrollClosed</c>), sumy roczne liczą tę listę, zajęcia ściągnięte
    /// w całości przechodzą w <c>paidOff</c>.
    /// </summary>
    public static async Task<NoContent> Handle(
        Guid id,
        PayrollVersion request,
        PayrollsHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.CloseAsync(id, request.RowVersion, cancellationToken);
        return TypedResults.NoContent();
    }
}

public static class SetPayrollPpkPaid
{
    /// <summary>Data przekazania wpłat PPK listy do instytucji finansowej; pusta usuwa datę.</summary>
    public sealed record Request(long? RowVersion, DateOnly? PaidOn);

    /// <summary>
    /// Data przekazania wpłat PPK zamkniętej listy (009 R19): od niej wpłaty pracodawcy są przychodem pracownika —
    /// liczy go lista z wypłatą w tym miesiącu nie wcześniej niż w tym dniu, a bez takiej listy PIT-11. Lista nie
    /// zamknięta → 409 <c>payrollNotClosed</c>; data przed datą wypłaty listy → 400 <c>paidOn</c> <c>beforePayment</c>,
    /// z przyszłości → <c>future</c>; zmiana po ujęciu wpłat jako przychodu na zamkniętej liście → 409
    /// <c>ppkIncomeTaxed</c>. Naliczone listy, których to dotyczy, stają się nieaktualne.
    /// </summary>
    public static async Task<NoContent> Handle(
        Guid id,
        Request request,
        PayrollsHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.PpkPaidAsync(id, request.RowVersion, request.PaidOn, cancellationToken);
        return TypedResults.NoContent();
    }
}

public static class ListPayrollMembers
{
    /// <summary>
    /// Skład listy: umowy z osobą, wyłączenie (<c>Excluded</c>) i wypłata (<c>EntryId</c>, gdy naliczona).
    /// </summary>
    public sealed record Item(
        Guid EmploymentId,
        string EmploymentNumber,
        Guid EmployeeId,
        string EmployeeNumber,
        string? Acronym,
        string FirstName,
        string LastName,
        bool Excluded,
        Guid? EntryId
    );

    public static async Task<Ok<List<Item>>> Handle(
        Guid id,
        PayrollsHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.MembersAsync(id, cancellationToken));
}

public static class SetPayrollMembers
{
    /// <summary>
    /// Wyłączenie umów z listy (<c>Excluded</c> = true) albo przywrócenie (false). Umowy muszą być w składzie
    /// (<c>employmentIds</c> <c>notFound</c>); zamknięta lista → 409 <c>closed</c>; naliczona staje się nieaktualna.
    /// </summary>
    public sealed record Request(long? RowVersion, Guid[]? EmploymentIds, bool? Excluded);

    public static async Task<NoContent> Handle(
        Guid id,
        Request request,
        PayrollsHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.SetMembersAsync(id, request, cancellationToken);
        return TypedResults.NoContent();
    }
}

public sealed class PayrollsHandler(DbSession db)
{
    public async Task<List<ListPayrolls.Item>> ListAsync(
        Guid companyId,
        int year,
        CancellationToken cancellationToken
    ) =>
        await db.QueryAsync(
            """
            SELECT id, number, kind, period_from, period_to, payment_date, payment_date_late, corrects_payroll_id,
                   corrects_number, status, is_stale, entry_count, gross, net, payout, row_version
            FROM payroll_app.payroll_list($1, $2)
            """,
            [Db.Param(companyId), Db.Param(year)],
            reader => new ListPayrolls.Item(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetFieldValue<DateOnly>(3),
                reader.GetFieldValue<DateOnly>(4),
                reader.GetFieldValue<DateOnly>(5),
                reader.GetBoolean(6),
                reader.IsDBNull(7) ? null : reader.GetGuid(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.GetString(9),
                reader.GetBoolean(10),
                reader.GetInt32(11),
                reader.GetDecimal(12),
                reader.GetDecimal(13),
                reader.GetDecimal(14),
                reader.GetInt64(15)
            ),
            cancellationToken
        );

    public async Task<Guid> CreateAsync(
        Guid companyId,
        CreatePayroll.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<Guid>(
            "CALL payroll_app.payroll_create($1, $2, $3, $4, $5, NULL)",
            [
                Db.Param(companyId),
                Db.Param(request.Kind),
                Db.Param(request.Period),
                Db.Param(request.PaymentDate),
                Db.Param(request.CorrectsPayrollId),
            ],
            cancellationToken
        );

    public async Task<GetPayroll.Response> GetAsync(Guid id, CancellationToken cancellationToken) =>
        (
            await db.QueryAsync(
                """
                SELECT id, company_id, number, kind, period_from, period_to, payment_date, payment_date_late,
                       corrects_payroll_id, corrects_number, status, is_stale, calculated_at, closed_at, ppk_paid_on,
                       entry_count, warning_count, blocked_count, member_count, excluded_count, gross,
                       employee_contributions, health, pit, ppk_employee, net, deductions, payout, employer_cost,
                       row_version
                FROM payroll_app.payroll_get($1)
                """,
                [Db.Param(id)],
                reader => new GetPayroll.Response(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetFieldValue<DateOnly>(4),
                    reader.GetFieldValue<DateOnly>(5),
                    reader.GetFieldValue<DateOnly>(6),
                    reader.GetBoolean(7),
                    reader.IsDBNull(8) ? null : reader.GetGuid(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.GetString(10),
                    reader.GetBoolean(11),
                    reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12),
                    reader.IsDBNull(13) ? null : reader.GetFieldValue<DateTimeOffset>(13),
                    reader.IsDBNull(14) ? null : reader.GetFieldValue<DateOnly>(14),
                    reader.GetInt32(15),
                    reader.GetInt32(16),
                    reader.GetInt32(17),
                    reader.GetInt32(18),
                    reader.GetInt32(19),
                    reader.GetDecimal(20),
                    reader.GetDecimal(21),
                    reader.GetDecimal(22),
                    reader.GetDecimal(23),
                    reader.GetDecimal(24),
                    reader.GetDecimal(25),
                    reader.GetDecimal(26),
                    reader.GetDecimal(27),
                    reader.GetDecimal(28),
                    reader.GetInt64(29)
                ),
                cancellationToken
            )
        ).Single();

    public async Task DeleteAsync(Guid id, long? rowVersion, CancellationToken cancellationToken) =>
        await db.ScalarAsync<object>(
            "CALL payroll_app.payroll_delete($1, $2)",
            [Db.Param(id), Db.Param(rowVersion)],
            cancellationToken
        );

    public async Task CalculateAsync(
        Guid id,
        long? rowVersion,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<object>(
            "CALL payroll_app.payroll_calculate($1, $2)",
            [Db.Param(id), Db.Param(rowVersion)],
            cancellationToken
        );

    public async Task CloseAsync(Guid id, long? rowVersion, CancellationToken cancellationToken) =>
        await db.ScalarAsync<object>(
            "CALL payroll_app.payroll_close($1, $2)",
            [Db.Param(id), Db.Param(rowVersion)],
            cancellationToken
        );

    public async Task PpkPaidAsync(
        Guid id,
        long? rowVersion,
        DateOnly? paidOn,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<object>(
            "CALL payroll_app.payroll_ppk_paid($1, $2, $3)",
            [Db.Param(id), Db.Param(rowVersion), Db.Param(paidOn)],
            cancellationToken
        );

    public async Task<List<ListPayrollMembers.Item>> MembersAsync(
        Guid id,
        CancellationToken cancellationToken
    ) =>
        await db.QueryAsync(
            """
            SELECT employment_id, employment_number, employee_id, employee_number, acronym, first_name, last_name,
                   excluded, entry_id
            FROM payroll_app.payroll_member_list($1)
            """,
            [Db.Param(id)],
            reader => new ListPayrollMembers.Item(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetGuid(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetBoolean(7),
                reader.IsDBNull(8) ? null : reader.GetGuid(8)
            ),
            cancellationToken
        );

    public async Task SetMembersAsync(
        Guid id,
        SetPayrollMembers.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<object>(
            "CALL payroll_app.payroll_members_set($1, $2, $3, $4)",
            [
                Db.Param(id),
                Db.Param(request.RowVersion),
                Db.Param(request.EmploymentIds ?? []),
                Db.Param(request.Excluded),
            ],
            cancellationToken
        );
}
