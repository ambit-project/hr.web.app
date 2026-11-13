using System.Net;
using HrWebApp.Api.Tests.Database;
using HrWebApp.Api.Tests.Infrastructure;
using PdfSharp.Pdf.IO;
using static HrWebApp.Api.Tests.Database.PayrollKit;

namespace HrWebApp.Api.Tests.Features.Documents;

[Collection(DatabaseCollection.Name)]
public sealed class PayslipPdfHttpTests(DatabaseFixture database)
{
    private readonly DocumentsKit _kit = new(database);
    private PayrollKit Payroll => _kit.Payroll;

    [Fact]
    public async Task Kadry_download_a_payslip_pdf_of_a_closed_list_and_pracownik_gets_403_on_others()
    {
        using var kadry = await database.Api.ClientAsAsync("kadry");
        using var pracownik = await database.Api.ClientAsAsync("pracownik");
        var company = await Payroll.CompanyAsync();
        var anna = await _kit.PersonAsync(company, "Żółkiewska", D(1990, 5, 10));
        var job = await Payroll.EmploymentAsync(
            anna,
            "employment",
            D(2026, 1, 1),
            null,
            "monthly",
            7000m,
            EmploymentInsurances
        );
        var october = await Payroll.MonthAsync(company, 10);
        var entry = await Payroll.EntryAsync(october, job);
        var november = await Payroll.MonthAsync(company, 11, close: false);
        var open = await Payroll.EntryAsync(november, job);

        var pdf = await kadry.GetAsync($"/api/payroll-entries/{entry.Id}/payslip/pdf");
        var notClosed = await kadry.GetAsync($"/api/payroll-entries/{open.Id}/payslip/pdf");
        var foreign = await pracownik.GetAsync($"/api/payroll-entries/{entry.Id}/payslip/pdf");

        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-store", pdf.Headers.CacheControl?.ToString());
        Assert.EndsWith(".pdf", pdf.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? "");
        var bytes = await pdf.Content.ReadAsByteArrayAsync();
        using var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
        Assert.Equal(1, document.PageCount);
        Assert.Contains("Żółkiewska", document.Info.Title);
        Assert.Equal(HttpStatusCode.Conflict, notClosed.StatusCode);
        Assert.Contains("notClosed", await notClosed.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
    }
}
