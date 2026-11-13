using System.Text.Json;
using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HrWebApp.Api.Features.Documents;

public static class GetPayslip
{
    /// <summary>
    /// Pasek wypłaty z zamkniętej listy (lista niezamknięta → 409 <c>notClosed</c>): spółka, osoba, umowa, lista,
    /// brutto, netto, do wypłaty, podstawy (ZUS, zdrowotna, PIT) i koszty, pozycje w kolejności naliczenia. Kadry
    /// i Administrator — każda wypłata (audyt odczytu); Pracownik — tylko swoja. <c>PayrollKind</c>:
    /// <c>employment</c>, <c>mandate</c>, <c>specificWork</c>, <c>correction</c>; <c>ContractKind</c>:
    /// <c>employment</c>, <c>mandate</c>, <c>specific_work</c>.
    /// </summary>
    public sealed record Response(
        Guid Id,
        Guid PayrollId,
        string PayrollNumber,
        string PayrollKind,
        DateOnly PeriodFrom,
        DateOnly PeriodTo,
        DateOnly PaymentDate,
        string CompanyName,
        string CompanyNip,
        string CompanyAddress,
        Guid EmployeeId,
        string EmployeeNumber,
        string FirstName,
        string LastName,
        string EmploymentNumber,
        string ContractKind,
        decimal Gross,
        decimal Net,
        decimal Payout,
        decimal ZusBase,
        decimal HealthBase,
        decimal TaxBase,
        decimal Costs,
        List<Line> Lines
    );

    /// <summary>
    /// Pozycja paska jak w wypłacie listy płac (kody i strony — <c>GetPayrollEntry</c>); <c>Name</c> — nazwa
    /// składnika płacowego (pusta dla pozycji silnika).
    /// </summary>
    public sealed record Line(
        int Seq,
        string Code,
        string Side,
        string? Name,
        decimal? Base,
        decimal? Rate,
        decimal Amount
    );

    public static async Task<Ok<Response>> Handle(
        Guid id,
        PayslipsHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.GetAsync(id, cancellationToken));
}

public static class ListPayslips
{
    /// <summary>
    /// Paski kartoteki — wypłaty z zamkniętych list od najnowszej daty wypłaty (odczyt w audycie). Kadry
    /// i Administrator — każda kartoteka; Pracownik — tylko swoja.
    /// </summary>
    public sealed record Item(
        Guid Id,
        string PayrollNumber,
        string PayrollKind,
        DateOnly PeriodFrom,
        DateOnly PaymentDate,
        string EmploymentNumber,
        string ContractKind,
        decimal Gross,
        decimal Net,
        decimal Payout
    );

    public static async Task<Ok<List<Item>>> HandleEmployee(
        Guid id,
        PayslipsHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.ListAsync(id, cancellationToken));

    public static async Task<Ok<List<Item>>> HandleMy(
        Guid employeeId,
        PayslipsHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.ListAsync(employeeId, cancellationToken));
}

public static class GetWageCard
{
    /// <summary>
    /// Karta wynagrodzeń kartoteki za rok (Kadry, Administrator; odczyt w audycie): wiersz na każdą wypłatę zamkniętej
    /// listy z datą wypłaty w roku. Składki pracodawcy z FP i FGŚP; PIT — zaliczka albo ryczałt.
    /// </summary>
    public sealed record Item(
        Guid EntryId,
        string PayrollNumber,
        string PayrollKind,
        DateOnly PeriodFrom,
        DateOnly PaymentDate,
        string EmploymentNumber,
        string ContractKind,
        decimal Gross,
        decimal ZusBase,
        decimal EmployeeContributions,
        decimal EmployerContributions,
        decimal Health,
        decimal Costs,
        decimal Pit,
        decimal PpkEmployee,
        decimal PpkEmployer,
        decimal Deductions,
        decimal Net,
        decimal Payout
    );

    public static async Task<Ok<List<Item>>> Handle(
        Guid id,
        int year,
        PayslipsHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.WageCardAsync(id, year, cancellationToken));
}

public sealed class PayslipsHandler(DbSession db)
{
    public async Task<GetPayslip.Response> GetAsync(Guid id, CancellationToken cancellationToken) =>
        (
            await db.QueryAsync(
                """
                SELECT id, payroll_id, payroll_number, payroll_kind, period_from, period_to, payment_date, company_name,
                       company_nip, company_address, employee_id, employee_number, first_name, last_name,
                       employment_number, contract_kind, gross, net, payout, zus_base, health_base, tax_base, costs,
                       lines::text
                FROM documents_app.payslip($1)
                """,
                [Db.Param(id)],
                reader => new GetPayslip.Response(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetFieldValue<DateOnly>(4),
                    reader.GetFieldValue<DateOnly>(5),
                    reader.GetFieldValue<DateOnly>(6),
                    reader.GetString(7),
                    reader.GetString(8),
                    reader.GetString(9),
                    reader.GetGuid(10),
                    reader.GetString(11),
                    reader.GetString(12),
                    reader.GetString(13),
                    reader.GetString(14),
                    reader.GetString(15),
                    reader.GetDecimal(16),
                    reader.GetDecimal(17),
                    reader.GetDecimal(18),
                    reader.GetDecimal(19),
                    reader.GetDecimal(20),
                    reader.GetDecimal(21),
                    reader.GetDecimal(22),
                    JsonSerializer.Deserialize<List<GetPayslip.Line>>(
                        reader.GetString(23),
                        JsonSerializerOptions.Web
                    ) ?? []
                ),
                cancellationToken
            )
        ).Single();

    public async Task<List<ListPayslips.Item>> ListAsync(
        Guid employeeId,
        CancellationToken cancellationToken
    ) =>
        await db.QueryAsync(
            """
            SELECT id, payroll_number, payroll_kind, period_from, payment_date, employment_number, contract_kind, gross,
                   net, payout
            FROM documents_app.my_payslips($1)
            """,
            [Db.Param(employeeId)],
            reader => new ListPayslips.Item(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetFieldValue<DateOnly>(3),
                reader.GetFieldValue<DateOnly>(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetDecimal(7),
                reader.GetDecimal(8),
                reader.GetDecimal(9)
            ),
            cancellationToken
        );

    public async Task<List<GetWageCard.Item>> WageCardAsync(
        Guid employeeId,
        int year,
        CancellationToken cancellationToken
    ) =>
        await db.QueryAsync(
            """
            SELECT entry_id, payroll_number, payroll_kind, period_from, payment_date, employment_number, contract_kind,
                   gross, zus_base, employee_contributions, employer_contributions, health, costs, pit, ppk_employee,
                   ppk_employer, deductions, net, payout
            FROM documents_app.wage_card($1, $2)
            """,
            [Db.Param(employeeId), Db.Param(year)],
            reader => new GetWageCard.Item(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetFieldValue<DateOnly>(3),
                reader.GetFieldValue<DateOnly>(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetDecimal(7),
                reader.GetDecimal(8),
                reader.GetDecimal(9),
                reader.GetDecimal(10),
                reader.GetDecimal(11),
                reader.GetDecimal(12),
                reader.GetDecimal(13),
                reader.GetDecimal(14),
                reader.GetDecimal(15),
                reader.GetDecimal(16),
                reader.GetDecimal(17),
                reader.GetDecimal(18)
            ),
            cancellationToken
        );
}
