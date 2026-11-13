using System.Text;
using System.Text.Json;
using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HrWebApp.Api.Features.Documents;

/// <summary>
/// Treść PIT-11(29) z dnia wygenerowania. <c>Positions</c> — pozycje formularza pod nazwami elementów schematu
/// 13535 (<c>P_11</c>, <c>P_28</c>–<c>P_65</c>, <c>P_95</c>–<c>P_97</c>, <c>P_109</c>–<c>P_122</c>); brak pozycji
/// = pole puste. <c>Missing</c>: <c>pesel</c>, <c>address</c>, <c>taxOffice</c> — bez nich formularza nie można
/// oznaczyć ani pobrać XML. <c>PayrollIds</c> — zamknięte listy źródłowe.
/// </summary>
public sealed record Pit11Data(
    string Form,
    int Year,
    Pit11Payer Payer,
    Pit11Taxpayer Taxpayer,
    Pit11TaxOffice? TaxOffice,
    Dictionary<string, decimal> Positions,
    List<Guid> PayrollIds,
    int EntryCount,
    List<string> Missing
);

/// <summary>Płatnik (część B): spółka.</summary>
public sealed record Pit11Payer(string Nip, string Name);

/// <summary>Podatnik (część C) bez PESEL — PESEL jest w <c>GetTaxForm.Response</c>.</summary>
public sealed record Pit11Taxpayer(
    string FirstName,
    string LastName,
    DateOnly BirthDate,
    Pit11Address? Address
);

/// <summary>Adres zamieszkania (poz. 19–27); ulica, dom i lokal z pola ulicy kartoteki.</summary>
public sealed record Pit11Address(
    string Country,
    string Street,
    string? House,
    string? Apartment,
    string City,
    string PostalCode
);

/// <summary>Urząd skarbowy podatnika (część A, poz. 6).</summary>
public sealed record Pit11TaxOffice(string Code, string Name);

public static class ListPit11
{
    /// <summary>
    /// PIT-11 spółki za rok: kartoteki z wypłatą do PIT-11 (zamknięte listy z datą wypłaty w roku, bez ryczałtu) albo
    /// z formularzem, ostatnia wersja (puste <c>TaxFormId</c> — jeszcze nie wygenerowano), cel (<c>filing</c>,
    /// <c>correction</c>), wysłanie do urzędu, przekazanie podatnikowi (<c>GivenHow</c>: <c>paper</c>,
    /// <c>email</c>, <c>portal</c>), brakujące dane i <c>IsOutdated</c> — dane list zmieniły się po wygenerowaniu.
    /// </summary>
    public sealed record Item(
        Guid EmployeeId,
        string EmployeeNumber,
        string FirstName,
        string LastName,
        Guid? TaxFormId,
        int? Version,
        string? Purpose,
        DateTimeOffset? GeneratedAt,
        DateOnly? SentToOfficeAt,
        DateOnly? GivenToEmployeeAt,
        string? GivenHow,
        List<string> Missing,
        bool IsOutdated
    );

    public static async Task<Ok<List<Item>>> Handle(
        Guid id,
        int year,
        TaxFormsHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.ListAsync(id, year, cancellationToken));
}

public static class GeneratePit11
{
    /// <summary>
    /// Generowanie PIT-11 za rok dla wszystkich kartotek spółki z wypłatą do PIT-11 albo jednej
    /// (<c>EmployeeId</c>: <c>notFound</c> — spoza spółki, <c>noPayments</c> — bez wypłat do PIT-11). Formularz
    /// niewysłany jest zastępowany, po wysłaniu zmiana to nowa wersja — korekta. <c>Changed</c> — liczba nowych
    /// i zmienionych formularzy.
    /// </summary>
    public sealed record Request(Guid? EmployeeId);

    public sealed record Response(int Changed);

    public static async Task<Ok<Response>> Handle(
        Guid id,
        int year,
        Request request,
        TaxFormsHandler handler,
        CancellationToken cancellationToken
    ) =>
        TypedResults.Ok(
            new Response(
                await handler.GenerateAsync(id, year, request.EmployeeId, cancellationToken)
            )
        );
}

public static class GetTaxForm
{
    /// <summary>
    /// Formularz PIT-11 (odczyt w audycie): treść, PESEL z kartoteki, wysłanie, przekazanie, czy ostatnia wersja.
    /// Pracownik — tylko swój, wysłany albo przekazany (inny → 404).
    /// </summary>
    public sealed record Response(
        Guid Id,
        Guid CompanyId,
        Guid EmployeeId,
        string EmployeeNumber,
        int Year,
        int Version,
        string Purpose,
        string? Pesel,
        DateTimeOffset GeneratedAt,
        DateOnly? SentToOfficeAt,
        DateOnly? GivenToEmployeeAt,
        string? GivenHow,
        bool IsLatest,
        Pit11Data Data
    );

    public static async Task<Ok<Response>> Handle(
        Guid id,
        TaxFormsHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.GetAsync(id, cancellationToken));
}

public static class GetTaxFormXml
{
    /// <summary>
    /// XML PIT-11(29) zgodny ze schematem <c>http://crd.gov.pl/wzor/2024/10/15/13535/</c> (plik do wysłania przez
    /// e-Deklaracje — wysyłki z systemu nie ma). Brak PESEL, adresu albo urzędu → 409 <c>missingData</c>.
    /// </summary>
    public static async Task<Results<FileContentHttpResult, ProblemHttpResult>> Handle(
        Guid id,
        TaxFormsHandler handler,
        CancellationToken cancellationToken
    )
    {
        var form = await handler.GetAsync(id, cancellationToken);
        if (form.Data.Missing.Count > 0 || form.Pesel is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Conflict",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "U0003",
                    ["reason"] = "missingData",
                }
            );
        }

        var xml = Pit11Xml.Build(form);
        return TypedResults.File(
            Encoding.UTF8.GetBytes(xml),
            "application/xml",
            $"PIT-11_{form.Year}_{form.EmployeeNumber}_v{form.Version}.xml"
        );
    }
}

public static class MarkTaxFormSent
{
    /// <summary>
    /// Oznaczenie wysłania do urzędu skarbowego (<c>SentAt</c>: <c>required</c>, <c>future</c>). Tylko ostatnia wersja
    /// (409 <c>notLatest</c>), raz (409 <c>sent</c>), kompletna (409 <c>missingData</c>).
    /// </summary>
    public sealed record Request(DateOnly? SentAt);

    public static async Task<NoContent> Handle(
        Guid id,
        Request request,
        TaxFormsHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.MarkSentAsync(id, request.SentAt, cancellationToken);
        return TypedResults.NoContent();
    }
}

public static class MarkTaxFormGiven
{
    /// <summary>
    /// Oznaczenie przekazania podatnikowi (<c>GivenAt</c>: <c>required</c>, <c>future</c>; <c>GivenHow</c>:
    /// <c>paper</c>, <c>email</c>, <c>portal</c>). Można poprawić. Niekompletny → 409 <c>missingData</c>.
    /// Przekazany formularz pracownik widzi w „Moich dokumentach”.
    /// </summary>
    public sealed record Request(DateOnly? GivenAt, string? GivenHow);

    public static async Task<NoContent> Handle(
        Guid id,
        Request request,
        TaxFormsHandler handler,
        CancellationToken cancellationToken
    )
    {
        await handler.MarkGivenAsync(id, request, cancellationToken);
        return TypedResults.NoContent();
    }
}

public static class ListTaxForms
{
    /// <summary>
    /// PIT-11 kartoteki, wszystkie wersje od najnowszego roku. Kadry i Administrator — każda kartoteka; Pracownik —
    /// tylko swoja, wersje wysłane albo przekazane.
    /// </summary>
    public sealed record Item(
        Guid Id,
        int Year,
        int Version,
        string Purpose,
        DateTimeOffset GeneratedAt,
        DateOnly? SentToOfficeAt,
        DateOnly? GivenToEmployeeAt,
        string? GivenHow
    );

    public static async Task<Ok<List<Item>>> HandleEmployee(
        Guid id,
        TaxFormsHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.ListForEmployeeAsync(id, cancellationToken));

    public static async Task<Ok<List<Item>>> HandleMy(
        Guid employeeId,
        TaxFormsHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.ListForEmployeeAsync(employeeId, cancellationToken));
}

public static class GetTaxSummary
{
    /// <summary>
    /// Zestawienie spółki za rok — pomoc do PIT-4R i PIT-8AR (bez XML): dla każdego miesiąca daty wypłaty zamkniętych
    /// list zaliczki z umów o pracę, zleceń i dzieł oraz ryczałt ze zleceń i dzieł, liczba osób. Zawsze 12 miesięcy.
    /// </summary>
    public sealed record Item(
        int Month,
        decimal EmploymentAdvances,
        decimal MandateAdvances,
        decimal SpecificWorkAdvances,
        decimal AdvancesTotal,
        int AdvancePeople,
        decimal MandateFlatTax,
        decimal SpecificWorkFlatTax,
        decimal FlatTaxTotal,
        int FlatTaxPeople
    );

    public static async Task<Ok<List<Item>>> Handle(
        Guid id,
        int year,
        TaxFormsHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.SummaryAsync(id, year, cancellationToken));
}

public static class GetConsentDocument
{
    /// <summary>
    /// Treść dokumentu zgody na potrącenie do podpisu (KP art. 91): pracodawca, pracownik z adresem zamieszkania,
    /// składnik (<c>DeductionCategory</c>: <c>voluntaryEmployer</c>, <c>voluntaryOther</c>, …), kwota miesięczna,
    /// okres, data podpisania. Kadry i Administrator — każda; Pracownik — tylko swoja.
    /// </summary>
    public sealed record Response(
        Guid Id,
        string CompanyName,
        string CompanyNip,
        string CompanyAddress,
        Guid EmployeeId,
        string EmployeeNumber,
        string FirstName,
        string LastName,
        string? EmployeeAddress,
        string ComponentCode,
        string ComponentName,
        string? DeductionCategory,
        decimal MonthlyAmount,
        DateOnly ValidFrom,
        DateOnly? ValidTo,
        DateOnly? SignedAt
    );

    public static async Task<Ok<Response>> Handle(
        Guid id,
        TaxFormsHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.ConsentDocumentAsync(id, cancellationToken));
}

public sealed class TaxFormsHandler(DbSession db)
{
    public async Task<List<ListPit11.Item>> ListAsync(
        Guid companyId,
        int year,
        CancellationToken cancellationToken
    ) =>
        await db.QueryAsync(
            """
            SELECT employee_id, employee_number, first_name, last_name, tax_form_id, version, purpose, generated_at,
                   sent_to_office_at, given_to_employee_at, given_how, missing, is_outdated
            FROM documents_app.tax_form_list($1, $2)
            """,
            [Db.Param(companyId), Db.Param(year)],
            reader => new ListPit11.Item(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetGuid(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
                reader.IsDBNull(8) ? null : reader.GetFieldValue<DateOnly>(8),
                reader.IsDBNull(9) ? null : reader.GetFieldValue<DateOnly>(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                [.. reader.GetFieldValue<string[]>(11)],
                reader.GetBoolean(12)
            ),
            cancellationToken
        );

    public async Task<int> GenerateAsync(
        Guid companyId,
        int year,
        Guid? employeeId,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<int>(
            "CALL documents_app.pit11_generate($1, $2, $3, NULL)",
            [Db.Param(companyId), Db.Param(year), Db.Param(employeeId)],
            cancellationToken
        );

    public async Task<GetTaxForm.Response> GetAsync(Guid id, CancellationToken cancellationToken) =>
        (
            await db.QueryAsync(
                """
                SELECT id, company_id, employee_id, employee_number, year, version, purpose, pesel, generated_at,
                       sent_to_office_at, given_to_employee_at, given_how, is_latest, data::text
                FROM documents_app.tax_form_get($1)
                """,
                [Db.Param(id)],
                reader => new GetTaxForm.Response(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetGuid(2),
                    reader.GetString(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.GetFieldValue<DateTimeOffset>(8),
                    reader.IsDBNull(9) ? null : reader.GetFieldValue<DateOnly>(9),
                    reader.IsDBNull(10) ? null : reader.GetFieldValue<DateOnly>(10),
                    reader.IsDBNull(11) ? null : reader.GetString(11),
                    reader.GetBoolean(12),
                    JsonSerializer.Deserialize<Pit11Data>(
                        reader.GetString(13),
                        JsonSerializerOptions.Web
                    )!
                ),
                cancellationToken
            )
        ).Single();

    public async Task MarkSentAsync(
        Guid id,
        DateOnly? sentAt,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<object>(
            "CALL documents_app.pit11_mark_sent($1, $2)",
            [Db.Param(id), Db.Param(sentAt)],
            cancellationToken
        );

    public async Task MarkGivenAsync(
        Guid id,
        MarkTaxFormGiven.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<object>(
            "CALL documents_app.pit11_mark_given($1, $2, $3)",
            [Db.Param(id), Db.Param(request.GivenAt), Db.Param(request.GivenHow)],
            cancellationToken
        );

    public async Task<List<ListTaxForms.Item>> ListForEmployeeAsync(
        Guid employeeId,
        CancellationToken cancellationToken
    ) =>
        await db.QueryAsync(
            """
            SELECT id, year, version, purpose, generated_at, sent_to_office_at, given_to_employee_at, given_how
            FROM documents_app.my_tax_forms($1)
            """,
            [Db.Param(employeeId)],
            reader => new ListTaxForms.Item(
                reader.GetGuid(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetFieldValue<DateTimeOffset>(4),
                reader.IsDBNull(5) ? null : reader.GetFieldValue<DateOnly>(5),
                reader.IsDBNull(6) ? null : reader.GetFieldValue<DateOnly>(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)
            ),
            cancellationToken
        );

    public async Task<List<GetTaxSummary.Item>> SummaryAsync(
        Guid companyId,
        int year,
        CancellationToken cancellationToken
    ) =>
        await db.QueryAsync(
            """
            SELECT month, employment_advances, mandate_advances, specific_work_advances, advances_total,
                   advance_people, mandate_flat_tax, specific_work_flat_tax, flat_tax_total, flat_tax_people
            FROM documents_app.company_tax_summary($1, $2)
            """,
            [Db.Param(companyId), Db.Param(year)],
            reader => new GetTaxSummary.Item(
                reader.GetInt32(0),
                reader.GetDecimal(1),
                reader.GetDecimal(2),
                reader.GetDecimal(3),
                reader.GetDecimal(4),
                reader.GetInt32(5),
                reader.GetDecimal(6),
                reader.GetDecimal(7),
                reader.GetDecimal(8),
                reader.GetInt32(9)
            ),
            cancellationToken
        );

    public async Task<GetConsentDocument.Response> ConsentDocumentAsync(
        Guid id,
        CancellationToken cancellationToken
    ) =>
        (
            await db.QueryAsync(
                """
                SELECT id, company_name, company_nip, company_address, employee_id, employee_number, first_name,
                       last_name, employee_address, component_code, component_name, deduction_category,
                       monthly_amount, valid_from, valid_to, signed_at
                FROM documents_app.consent_document($1)
                """,
                [Db.Param(id)],
                reader => new GetConsentDocument.Response(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetGuid(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8),
                    reader.GetString(9),
                    reader.GetString(10),
                    reader.IsDBNull(11) ? null : reader.GetString(11),
                    reader.GetDecimal(12),
                    reader.GetFieldValue<DateOnly>(13),
                    reader.IsDBNull(14) ? null : reader.GetFieldValue<DateOnly>(14),
                    reader.IsDBNull(15) ? null : reader.GetFieldValue<DateOnly>(15)
                ),
                cancellationToken
            )
        ).Single();
}
