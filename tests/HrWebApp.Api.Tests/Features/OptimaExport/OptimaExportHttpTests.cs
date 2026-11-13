using System.Net;
using HrWebApp.Api.Features.OptimaExport;
using HrWebApp.Api.Tests.Database;
using HrWebApp.Api.Tests.Infrastructure;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using static HrWebApp.Api.Tests.Database.PayrollKit;

namespace HrWebApp.Api.Tests.Features.OptimaExport;

[Collection(DatabaseCollection.Name)]
public sealed class OptimaExportHttpTests(DatabaseFixture database)
{
    private readonly DocumentsKit _kit = new(database);
    private PayrollKit Payroll => _kit.Payroll;

    [Fact]
    public async Task Kadry_download_the_selected_people_as_optima_xls_and_each_pesel_read_is_audited()
    {
        using var kadry = await database.Api.ClientAsAsync("kadry");
        var company = await Payroll.CompanyAsync();
        var birth = D(1990, 5, 10);
        var anna = await _kit.PersonAsync(company, "Annowska", birth);
        await _kit.PersonAsync(company, "Pominięta", D(1985, 1, 1));
        var other = await Payroll.CompanyAsync();
        var foreign = await _kit.PersonAsync(other, "Obca", D(1980, 1, 1));

        var response = await kadry.PostAsJsonAsync(
            "/api/employees/export/optima",
            new { companyId = company, employeeIds = new[] { anna, foreign } }
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            OptimaHrImportSheet.ContentType,
            response.Content.Headers.ContentType?.MediaType
        );
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        using var workbook = new HSSFWorkbook(await response.Content.ReadAsStreamAsync());
        var sheet = workbook.GetSheet(OptimaHrImportSheet.SheetName);
        Assert.Equal(1, sheet.LastRowNum);
        var cell = Cells(sheet);
        var acronym = await Payroll.AdminAsync<string>(
            "SELECT acronym FROM employees.employees WHERE id = $1",
            anna
        );
        var pesel = await Payroll.AdminAsync<string>(
            "SELECT ext.pgp_sym_decrypt(pesel_enc, $2) FROM employees.employees WHERE id = $1",
            anna,
            TestDb.DataKey
        );
        Assert.Equal(acronym, cell("Kod"));
        Assert.Equal("Annowska", cell("Nazwisko"));
        Assert.Equal("1990-05-10", cell("Data_urodzenia"));
        Assert.Equal(pesel, cell("Pesel"));
        Assert.Equal(OptimaEmployeeRow.Sex(pesel), cell("Plec"));
        Assert.Equal("Lipowa", cell("Ulica_zamieszkania"));
        Assert.Equal("1", cell("Nr_domu_zamieszkania"));
        Assert.Equal("2", cell("Nr_lokalu_zamieszkania"));
        Assert.Equal("Polska", cell("Kraj_zamieszkania"));
        Assert.Equal("0", cell("Archiwalny"));
        Assert.Equal("FIRMA", cell("Wydzial"));
        Assert.Equal("", cell("Rodzaj_umowy"));
        Assert.Equal(
            1L,
            await Payroll.AdminAsync<long>(
                "SELECT count(*) FROM platform.read_audit_entries WHERE employee_id = $1 AND resource = 'optima_export'",
                anna
            )
        );
    }

    [Fact]
    public async Task Without_selection_the_list_filter_decides_and_company_is_required()
    {
        using var kadry = await database.Api.ClientAsAsync("kadry");
        using var pracownik = await database.Api.ClientAsAsync("pracownik");
        var company = await Payroll.CompanyAsync();
        await _kit.PersonAsync(company, "Filtrowana", D(1990, 1, 1));
        await _kit.PersonAsync(company, "Inna", D(1991, 1, 1));

        var filtered = await kadry.PostAsJsonAsync(
            "/api/employees/export/optima",
            new { companyId = company, search = "Filtrow" }
        );
        var noCompany = await kadry.PostAsJsonAsync("/api/employees/export/optima", new { });
        var forbidden = await pracownik.PostAsJsonAsync(
            "/api/employees/export/optima",
            new { companyId = company }
        );

        using var workbook = new HSSFWorkbook(await filtered.Content.ReadAsStreamAsync());
        var sheet = workbook.GetSheet(OptimaHrImportSheet.SheetName);
        Assert.Equal(1, sheet.LastRowNum);
        Assert.Equal("Filtrowana", Cells(sheet)("Nazwisko"));
        Assert.Equal(HttpStatusCode.BadRequest, noCompany.StatusCode);
        Assert.Contains("companyId", await noCompany.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    private static Func<string, string> Cells(ISheet sheet)
    {
        var header = sheet.GetRow(0);
        var row = sheet.GetRow(1);
        return name =>
        {
            var index = header.Cells.FindIndex(c => c.StringCellValue == name);
            Assert.True(index >= 0, name);
            return row.GetCell(index)?.ToString() ?? "";
        };
    }
}
