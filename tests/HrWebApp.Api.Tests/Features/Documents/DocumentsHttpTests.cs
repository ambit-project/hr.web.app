using System.Net;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using HrWebApp.Api.Features.Documents;
using HrWebApp.Api.Tests.Database;
using HrWebApp.Api.Tests.Infrastructure;
using static HrWebApp.Api.Tests.Database.PayrollKit;

namespace HrWebApp.Api.Tests.Features.Documents;

[Collection(DatabaseCollection.Name)]
public sealed class DocumentsHttpTests(DatabaseFixture database)
{
    private readonly DocumentsKit _kit = new(database);
    private PayrollKit Payroll => _kit.Payroll;

    [Fact]
    public async Task Kadry_generate_pit11_and_every_xml_is_valid_against_the_ministry_schema()
    {
        using var kadry = await database.Api.ClientAsAsync("kadry");
        var company = await Payroll.CompanyAsync();
        var anna = await _kit.PersonAsync(company, "Anna", D(1990, 5, 10));
        var annaJob = await Payroll.EmploymentAsync(
            anna,
            "employment",
            D(2026, 1, 1),
            null,
            "monthly",
            7500m,
            EmploymentInsurances
        );
        await Payroll.Pit2Async(anna);
        await Payroll.PpkAsync(anna, D(2026, 4, 10));
        var piotr = await _kit.PersonAsync(company, "Piotr", D(2004, 3, 1));
        await Payroll.EmploymentAsync(
            piotr,
            "employment",
            D(2026, 6, 1),
            null,
            "monthly",
            2600m,
            EmploymentInsurances,
            fteNumerator: 1,
            fteDenominator: 2
        );
        await Payroll.EmploymentAsync(
            piotr,
            "mandate",
            D(2026, 10, 1),
            D(2026, 10, 31),
            "lumpSum",
            1000m,
            MandateInsurances,
            "flat20"
        );
        var ola = await _kit.PersonAsync(company, "Ola", D(2003, 7, 1));
        await Payroll.EmploymentAsync(
            ola,
            "employment",
            D(2026, 1, 1),
            null,
            "monthly",
            6000m,
            EmploymentInsurances,
            "increased"
        );
        await Payroll.DeclarationAsync(ola, "youthReliefWaiver", D(2026, 1, 1));
        var marek = await _kit.PersonAsync(company, "Marek", D(1985, 6, 1));
        await Payroll.EmploymentAsync(
            marek,
            "mandate",
            D(2026, 10, 1),
            D(2026, 12, 31),
            "lumpSum",
            3000m,
            MandateInsurances,
            "flat20"
        );
        var ewa = await _kit.PersonAsync(company, "Ewa", D(1980, 2, 2));
        await Payroll.EmploymentAsync(
            ewa,
            "specific_work",
            D(2026, 10, 1),
            D(2026, 10, 30),
            "lumpSum",
            2000m,
            NoInsurances,
            "author50"
        );
        var october = await Payroll.MonthAsync(company, 10);
        await Payroll.MonthAsync(company, 10, "mandate");
        await Payroll.MonthAsync(company, 10, "specificWork");

        var generated = await kadry.PostAsJsonAsync(
            $"/api/companies/{company}/pit11/2026",
            new { employeeId = (Guid?)null }
        );
        Assert.Equal(HttpStatusCode.OK, generated.StatusCode);
        Assert.Equal(
            5,
            (await generated.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("changed")
                .GetInt32()
        );
        var list = (
            await kadry.GetFromJsonAsync<JsonElement[]>($"/api/companies/{company}/pit11/2026")
        )!;
        Assert.Equal(5, list.Length);
        Assert.All(list, item => Assert.Equal("filing", item.GetProperty("purpose").GetString()));

        var schemas = Schemas();
        foreach (var item in list)
        {
            var id = item.GetProperty("taxFormId").GetGuid();
            var response = await kadry.GetAsync($"/api/tax-forms/{id}/xml");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/xml", response.Content.Headers.ContentType?.MediaType);
            Assert.StartsWith(
                "PIT-11_2026_",
                response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
            );
            Validate(await response.Content.ReadAsStringAsync(), schemas);
        }

        var annaForm = list.Single(i => i.GetProperty("employeeId").GetGuid() == anna);
        var annaId = annaForm.GetProperty("taxFormId").GetGuid();
        var form = await kadry.GetFromJsonAsync<JsonElement>($"/api/tax-forms/{annaId}");
        var positions = form.GetProperty("data").GetProperty("positions");
        var entry = await Payroll.EntryAsync(october, annaJob);
        Assert.Equal(
            entry.Gross + entry.Amount("PPK-PR"),
            positions.GetProperty("P_29").GetDecimal()
        );
        Assert.Equal(entry.Amount("PIT"), positions.GetProperty("P_33").GetDecimal());
        Assert.Matches("^\\d{11}$", form.GetProperty("pesel").GetString());
        Assert.True(form.GetProperty("isLatest").GetBoolean());
        var xml = XDocument.Parse(await kadry.GetStringAsync($"/api/tax-forms/{annaId}/xml"));
        XNamespace tns = Pit11Xml.Namespace;
        Assert.Equal("1", xml.Descendants(tns + "CelZlozenia").Single().Value);
        Assert.Equal("Lipowa", xml.Descendants(tns + "Ulica").Single().Value);

        var badSent = await kadry.PostAsJsonAsync(
            $"/api/tax-forms/{annaId}/sent",
            new { sentAt = (string?)null }
        );
        Assert.Equal(HttpStatusCode.BadRequest, badSent.StatusCode);
        Assert.Contains("sentAt", await badSent.Content.ReadAsStringAsync());
        var given = await kadry.PostAsJsonAsync(
            $"/api/tax-forms/{annaId}/given",
            new { givenAt = "2026-11-12", givenHow = "email" }
        );
        var sent = await kadry.PostAsJsonAsync(
            $"/api/tax-forms/{annaId}/sent",
            new { sentAt = "2026-11-12" }
        );
        var again = await kadry.PostAsJsonAsync(
            $"/api/tax-forms/{annaId}/sent",
            new { sentAt = "2026-11-12" }
        );
        Assert.Equal(HttpStatusCode.NoContent, given.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, sent.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("\"sent\"", await again.Content.ReadAsStringAsync());

        // Korekta po wysłaniu — wersja 2 z celem 2 w XML, także zgodna ze schematem.
        var bonus = await Payroll.ComponentTypeAsync("addition", "fixed");
        var correction = await Payroll.PayrollAsync(
            company,
            "correction",
            D(2026, 10, 1),
            D(2026, 11, 10),
            october
        );
        await Payroll.ComponentAsync(annaJob, bonus, 300m, D(2026, 10, 1), D(2026, 10, 31));
        await Payroll.CalculateAsync(correction);
        await Payroll.CloseAsync(correction);
        var regenerated = await kadry.PostAsJsonAsync(
            $"/api/companies/{company}/pit11/2026",
            new { employeeId = anna }
        );
        Assert.Equal(
            1,
            (await regenerated.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("changed")
                .GetInt32()
        );
        var versions = (
            await kadry.GetFromJsonAsync<JsonElement[]>($"/api/employees/{anna}/tax-forms")
        )!;
        Assert.Equal(2, versions.Length);
        Assert.Equal("correction", versions[0].GetProperty("purpose").GetString());
        var correctionXml = await kadry.GetStringAsync(
            $"/api/tax-forms/{versions[0].GetProperty("id").GetGuid()}/xml"
        );
        Validate(correctionXml, schemas);
        Assert.Equal(
            "2",
            XDocument.Parse(correctionXml).Descendants(tns + "CelZlozenia").Single().Value
        );

        var payslips = (
            await kadry.GetFromJsonAsync<JsonElement[]>($"/api/employees/{anna}/payslips")
        )!;
        Assert.Equal(2, payslips.Length);
        var payslip = await kadry.GetFromJsonAsync<JsonElement>(
            $"/api/payroll-entries/{entry.Id}/payslip"
        );
        Assert.Equal(entry.Payout, payslip.GetProperty("payout").GetDecimal());
        Assert.Contains(payslip.GetProperty("lines").EnumerateArray(), line =>
            line.GetProperty("code").GetString() == "ZASAD"
        );
        var card = (
            await kadry.GetFromJsonAsync<JsonElement[]>($"/api/employees/{anna}/wage-card/2026")
        )!;
        Assert.Equal(2, card.Length);
        var summary = (
            await kadry.GetFromJsonAsync<JsonElement[]>(
                $"/api/companies/{company}/tax-summary/2026"
            )
        )!;
        Assert.Equal(12, summary.Length);
        Assert.True(summary[9].GetProperty("advancesTotal").GetDecimal() > 0);
    }

    [Fact]
    public async Task Missing_pesel_blocks_the_xml_and_pracownik_gets_403_on_forms_of_others()
    {
        using var kadry = await database.Api.ClientAsAsync("kadry");
        using var pracownik = await database.Api.ClientAsAsync("pracownik");
        var company = await Payroll.CompanyAsync();
        var jan = await _kit.PersonAsync(company, "Jan", D(1985, 1, 1), withPesel: false);
        var job = await Payroll.EmploymentAsync(
            jan,
            "employment",
            D(2026, 1, 1),
            null,
            "monthly",
            7000m,
            EmploymentInsurances
        );
        var card = await Payroll.ComponentTypeAsync("deduction", "fixed", "voluntaryEmployer");
        var consent = await Payroll.ConsentAsync(jan, card, 50m, D(2026, 1, 1));
        var october = await Payroll.MonthAsync(company, 10);
        var entry = await Payroll.EntryAsync(october, job);
        await kadry.PostAsJsonAsync(
            $"/api/companies/{company}/pit11/2026",
            new { employeeId = jan }
        );
        var item = Assert.Single(
            (await kadry.GetFromJsonAsync<JsonElement[]>($"/api/companies/{company}/pit11/2026"))!
        );
        Assert.Equal("pesel", item.GetProperty("missing")[0].GetString());
        var id = item.GetProperty("taxFormId").GetGuid();
        var xml = await kadry.GetAsync($"/api/tax-forms/{id}/xml");
        Assert.Equal(HttpStatusCode.Conflict, xml.StatusCode);
        Assert.Contains("missingData", await xml.Content.ReadAsStringAsync());
        var noPayments = await kadry.PostAsJsonAsync(
            $"/api/companies/{company}/pit11/2026",
            new { employeeId = Guid.NewGuid() }
        );
        Assert.Equal(HttpStatusCode.BadRequest, noPayments.StatusCode);
        Assert.Contains("employeeId", await noPayments.Content.ReadAsStringAsync());
        var document = await kadry.GetFromJsonAsync<JsonElement>(
            $"/api/deduction-consents/{consent}/document"
        );
        Assert.Equal(50m, document.GetProperty("monthlyAmount").GetDecimal());
        Assert.Equal("voluntaryEmployer", document.GetProperty("deductionCategory").GetString());

        var own = await TestDb.AdminScalarAsync<Guid>(
            database,
            "SELECT l.employee_id FROM platform.user_employee_links l JOIN platform.users u ON u.id = l.user_id WHERE u.login = 'pracownik' LIMIT 1"
        );
        var form = await pracownik.GetAsync($"/api/tax-forms/{id}");
        var formXml = await pracownik.GetAsync($"/api/tax-forms/{id}/xml");
        var foreignList = await pracownik.GetAsync($"/api/my/{jan}/tax-forms");
        var foreignPayslips = await pracownik.GetAsync($"/api/my/{jan}/payslips");
        var foreignPayslip = await pracownik.GetAsync($"/api/payroll-entries/{entry.Id}/payslip");
        var generate = await pracownik.PostAsJsonAsync(
            $"/api/companies/{company}/pit11/2026",
            new { employeeId = (Guid?)null }
        );
        var companyList = await pracownik.GetAsync($"/api/companies/{company}/pit11/2026");
        var wageCard = await pracownik.GetAsync($"/api/employees/{jan}/wage-card/2026");
        var summary = await pracownik.GetAsync($"/api/companies/{company}/tax-summary/2026");
        var foreignConsent = await pracownik.GetAsync(
            $"/api/deduction-consents/{consent}/document"
        );
        var mine = await pracownik.GetAsync($"/api/my/{own}/tax-forms");
        var myPayslips = await pracownik.GetAsync($"/api/my/{own}/payslips");

        Assert.Equal(HttpStatusCode.Forbidden, form.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, formXml.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, foreignList.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, foreignPayslips.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, foreignPayslip.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, generate.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, companyList.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, wageCard.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, summary.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, foreignConsent.StatusCode);
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        Assert.Equal(HttpStatusCode.OK, myPayslips.StatusCode);
    }

    private static XmlSchemaSet Schemas()
    {
        var directory = RepoPaths.Combine("tests", "HrWebApp.Api.Tests", "Fixtures", "pit11-29");
        var schemas = new XmlSchemaSet { XmlResolver = new FixtureResolver(directory) };
        schemas.Add(Pit11Xml.Namespace, Path.Combine(directory, "schemat.xsd"));
        schemas.Compile();
        return schemas;
    }

    private static void Validate(string xml, XmlSchemaSet schemas)
    {
        var errors = new List<string>();
        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = schemas,
            DtdProcessing = DtdProcessing.Prohibit,
        };
        settings.ValidationEventHandler += (_, e) => errors.Add($"{e.Severity}: {e.Message}");
        using (var reader = XmlReader.Create(new StringReader(xml), settings))
        {
            while (reader.Read()) { }
        }

        Assert.True(errors.Count == 0, string.Join("\n", errors) + "\n" + xml);
    }

    /// <summary>Importy schematu (adresy crd.gov.pl) z kopii w repo — test nie sięga do sieci.</summary>
    private sealed class FixtureResolver(string directory) : XmlUrlResolver
    {
        public override object GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn) =>
            File.OpenRead(
                Path.Combine(
                    directory,
                    Uri.UnescapeDataString(Path.GetFileName(absoluteUri.AbsolutePath))
                )
            );
    }
}
