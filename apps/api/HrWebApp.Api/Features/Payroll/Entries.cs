using System.Text.Json;
using System.Text.Json.Nodes;
using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HrWebApp.Api.Features.Payroll;

/// <summary>
/// Ostrzeżenie wypłaty (nie błąd). <c>Code</c>: <c>pensionLimitReached</c> (limit 30-krotności),
/// <c>peselMissing</c> (FP i FGŚP bez sprawdzenia wieku), <c>sicknessBaseMissing</c> (zlecenie godzinowe bez
/// historii — podstawa 0), <c>unconfirmedHours</c> (niepotwierdzone godziny zlecenia nie są płacone),
/// <c>rateTimesHoursNotSupported</c> (składnik stawka × godziny na umowie o pracę), <c>leaveEntitlementMissing</c>
/// (koniec umowy bez limitu urlopu — bez ekwiwalentu), <c>deductionLimited</c> (potrącenie obcięte granicą albo
/// kwotą wolną), <c>negativeTaxBase</c>, <c>negativeCorrection</c>, <c>deductionsNotRecalculated</c> (korekta nie
/// liczy potrąceń). <c>Details</c> — obiekt JSON z wartościami ostrzeżenia (np. <c>limitedBase</c>, <c>count</c>,
/// <c>year</c>).
/// </summary>
public sealed record PayrollWarning(string Code, JsonObject Details);

public static class ListPayrollEntries
{
    /// <summary>
    /// Wypłaty listy (odczyt kwot osób trafia do audytu odczytu): osoba, umowa, brutto, składki społeczne pracownika,
    /// zdrowotna, PIT (zaliczka albo ryczałt), PPK pracownika, netto, potrącenia, do wypłaty, koszt pracodawcy,
    /// ostrzeżenia.
    /// </summary>
    public sealed record Item(
        Guid Id,
        Guid EmploymentId,
        string EmploymentNumber,
        Guid EmployeeId,
        string EmployeeNumber,
        string? Acronym,
        string FirstName,
        string LastName,
        decimal Gross,
        decimal EmployeeContributions,
        decimal Health,
        decimal Pit,
        decimal PpkEmployee,
        decimal Net,
        decimal Deductions,
        decimal Payout,
        decimal EmployerCost,
        List<PayrollWarning> Warnings
    );

    public static async Task<Ok<List<Item>>> Handle(
        Guid id,
        PayrollEntriesHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.ListAsync(id, cancellationToken));
}

public static class GetPayrollEntry
{
    /// <summary>
    /// Wypłata z pozycjami i śladem (odczyt trafia do audytu odczytu). <c>PayrollStatus</c>: <c>open</c>,
    /// <c>calculated</c>, <c>closed</c>. Godziny: wymiar, przepracowane, nadgodziny (umowa o pracę; zlecenie —
    /// potwierdzone godziny).
    /// </summary>
    public sealed record Response(
        Guid Id,
        Guid PayrollId,
        string PayrollNumber,
        string PayrollStatus,
        Guid EmploymentId,
        string EmploymentNumber,
        Guid EmployeeId,
        string EmployeeNumber,
        string FirstName,
        string LastName,
        Guid? TermsId,
        decimal Gross,
        decimal Net,
        decimal Payout,
        decimal? NormHours,
        decimal? WorkedHours,
        decimal? OvertimeHours,
        List<PayrollWarning> Warnings,
        List<Line> Lines
    );

    /// <summary>
    /// Pozycja wypłaty w kolejności naliczenia (<c>Seq</c>). <c>Side</c>: <c>income</c> (przychód),
    /// <c>employee</c> (po stronie pracownika: składki, zdrowotna, PIT, PPK), <c>employer</c> (koszt pracodawcy),
    /// <c>info</c> (informacyjna, nie zmienia kwot), <c>deduction</c> (potrącenie z netto). <c>Code</c>:
    /// <c>ZASAD</c>, <c>ZASAD-CH</c>, <c>ZASAD-NB</c>, <c>SW50</c>, <c>CHOR</c>, <c>ZAS-CH</c>, <c>ZAS-ZUS</c>,
    /// <c>URLOP-ZM</c>, <c>NADG50</c>, <c>NADG100</c>, <c>ZLEC</c>, <c>DZIELO</c>, <c>EKWIW</c>, <c>PODST-ZUS</c>,
    /// <c>PODST-CHOR</c>, <c>ZUS-E-P</c>, <c>ZUS-R-P</c>, <c>ZUS-C-P</c>, <c>ZUS-E-PR</c>, <c>ZUS-R-PR</c>,
    /// <c>ZUS-W-PR</c>, <c>FP</c>, <c>FGSP</c>, <c>PPK-P</c>, <c>PPK-PD</c>, <c>PPK-PR</c>, <c>PPK-PRD</c>,
    /// <c>ZDROW</c>, <c>ZWOL-MLODZI</c>, <c>KUP</c>, <c>KUP50</c>, <c>PODST-PIT</c>, <c>PIT-ZMN</c>, <c>PIT</c>,
    /// <c>PIT-RYCZ</c>, <c>ALIM</c>, <c>KOMOR</c> albo kod składnika płacowego (<c>ComponentName</c>). Stawka jako
    /// ułamek (0,0976 = 9,76%). Cechy przychodu (<c>InZusBase</c>, <c>InHealthBase</c>, <c>Taxable</c>,
    /// <c>InSicknessBase</c>) tylko przy przychodach. <c>Trace</c> — obiekt JSON ze źródłami: parametry prawne
    /// (<c>params</c>: kod → <c>value</c>, <c>validFrom</c>), warunki, nieobecności, godziny, oświadczenia, PPK,
    /// zajęcia, sumy roczne przed naliczeniem.
    /// </summary>
    public sealed record Line(
        int Seq,
        string Code,
        string Side,
        decimal? Base,
        decimal? Rate,
        decimal Amount,
        Guid? ComponentTypeId,
        string? ComponentName,
        bool? InZusBase,
        bool? InHealthBase,
        bool? Taxable,
        bool? InSicknessBase,
        JsonObject Trace
    );

    public static async Task<Ok<Response>> Handle(
        Guid id,
        PayrollEntriesHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.GetAsync(id, cancellationToken));
}

public static class GetYearTotals
{
    /// <summary>
    /// Sumy roczne kartoteki z zamkniętych list (odczyt trafia do audytu odczytu): podstawa emerytalno-rentowa (po
    /// okresie listy; limit 30-krotności), dochód PIT z umów o pracę i ze wszystkich umów, przychód zwolniony (ulgi),
    /// koszty 50% (autorskie) i koszty pracownicze (po dacie wypłaty).
    /// </summary>
    public sealed record Response(
        decimal PensionBase,
        decimal TaxBaseEmployment,
        decimal TaxBaseAll,
        decimal ExemptIncome,
        decimal AuthorCosts,
        decimal EmployeeCosts
    );

    public static async Task<Ok<Response>> Handle(
        Guid id,
        int year,
        PayrollEntriesHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.YearTotalsAsync(id, year, cancellationToken));
}

public sealed class PayrollEntriesHandler(DbSession db)
{
    public async Task<List<ListPayrollEntries.Item>> ListAsync(
        Guid payrollId,
        CancellationToken cancellationToken
    ) =>
        await db.QueryAsync(
            """
            SELECT id, employment_id, employment_number, employee_id, employee_number, acronym, first_name, last_name,
                   gross, employee_contributions, health, pit, ppk_employee, net, deductions, payout, employer_cost,
                   warnings::text
            FROM payroll_app.entry_list($1)
            """,
            [Db.Param(payrollId)],
            reader => new ListPayrollEntries.Item(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetGuid(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetDecimal(8),
                reader.GetDecimal(9),
                reader.GetDecimal(10),
                reader.GetDecimal(11),
                reader.GetDecimal(12),
                reader.GetDecimal(13),
                reader.GetDecimal(14),
                reader.GetDecimal(15),
                reader.GetDecimal(16),
                Warnings(reader.GetString(17))
            ),
            cancellationToken
        );

    public async Task<GetPayrollEntry.Response> GetAsync(
        Guid id,
        CancellationToken cancellationToken
    )
    {
        var lines = await db.QueryAsync(
            """
            SELECT seq, code, side, base, rate, amount, component_type_id, component_name, in_zus_base,
                   in_health_base, taxable, in_sickness_base, coalesce(trace, '{}')::text
            FROM payroll_app.entry_lines($1)
            """,
            [Db.Param(id)],
            reader => new GetPayrollEntry.Line(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetDecimal(3),
                reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                reader.GetDecimal(5),
                reader.IsDBNull(6) ? null : reader.GetGuid(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetBoolean(8),
                reader.IsDBNull(9) ? null : reader.GetBoolean(9),
                reader.IsDBNull(10) ? null : reader.GetBoolean(10),
                reader.IsDBNull(11) ? null : reader.GetBoolean(11),
                Parse(reader.GetString(12))
            ),
            cancellationToken
        );
        return (
            await db.QueryAsync(
                """
                SELECT id, payroll_id, payroll_number, payroll_status, employment_id, employment_number, employee_id,
                       employee_number, first_name, last_name, terms_id, gross, net, payout, norm_hours, worked_hours,
                       overtime_hours, warnings::text
                FROM payroll_app.entry_get($1)
                """,
                [Db.Param(id)],
                reader => new GetPayrollEntry.Response(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetGuid(4),
                    reader.GetString(5),
                    reader.GetGuid(6),
                    reader.GetString(7),
                    reader.GetString(8),
                    reader.GetString(9),
                    reader.IsDBNull(10) ? null : reader.GetGuid(10),
                    reader.GetDecimal(11),
                    reader.GetDecimal(12),
                    reader.GetDecimal(13),
                    reader.IsDBNull(14) ? null : reader.GetDecimal(14),
                    reader.IsDBNull(15) ? null : reader.GetDecimal(15),
                    reader.IsDBNull(16) ? null : reader.GetDecimal(16),
                    Warnings(reader.GetString(17)),
                    lines
                ),
                cancellationToken
            )
        ).Single();
    }

    public async Task<GetYearTotals.Response> YearTotalsAsync(
        Guid employeeId,
        int year,
        CancellationToken cancellationToken
    ) =>
        (
            await db.QueryAsync(
                """
                SELECT pension_base, tax_base_employment, tax_base_all, exempt_income, author_costs, employee_costs
                FROM payroll_app.year_totals($1, $2)
                """,
                [Db.Param(employeeId), Db.Param(year)],
                reader => new GetYearTotals.Response(
                    reader.GetDecimal(0),
                    reader.GetDecimal(1),
                    reader.GetDecimal(2),
                    reader.GetDecimal(3),
                    reader.GetDecimal(4),
                    reader.GetDecimal(5)
                ),
                cancellationToken
            )
        ).Single();

    private static JsonObject Parse(string json) => JsonNode.Parse(json)?.AsObject() ?? [];

    /// <summary><c>[{"code": …, …szczegóły}]</c> z bazy → kod i szczegóły bez kodu.</summary>
    private static List<PayrollWarning> Warnings(string json)
    {
        using var document = JsonDocument.Parse(json);
        var warnings = new List<PayrollWarning>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var details = new JsonObject();
            var code = string.Empty;
            foreach (var property in item.EnumerateObject())
            {
                if (property.NameEquals("code"))
                {
                    code = property.Value.GetString() ?? string.Empty;
                }
                else
                {
                    details[property.Name] = JsonNode.Parse(property.Value.GetRawText());
                }
            }

            warnings.Add(new PayrollWarning(code, details));
        }

        return warnings;
    }
}
