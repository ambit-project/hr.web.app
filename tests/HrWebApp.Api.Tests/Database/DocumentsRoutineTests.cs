using HrWebApp.Api.Tests.Infrastructure;
using Npgsql;
using static HrWebApp.Api.Tests.Database.PayrollKit;

namespace HrWebApp.Api.Tests.Database;

/// <summary>Dokumenty i PIT (010): pasek, karta wynagrodzeń, PIT-11 z zamkniętych list, wersje, zestawienie, role.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class DocumentsRoutineTests(DatabaseFixture database)
{
    private readonly DocumentsKit _kit = new(database);
    private PayrollKit Payroll => _kit.Payroll;

    [Fact]
    public async Task Employment_of_an_adult_fills_row_1_with_employer_ppk_contributions_and_health()
    {
        var company = await Payroll.CompanyAsync();
        var anna = await _kit.PersonAsync(company, "Anna", D(1990, 5, 10));
        var employment = await Payroll.EmploymentAsync(
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
        var september = await Payroll.MonthAsync(company, 9);
        // Wpłaty PPK września przekazane 10.10 — przychód na liście października (PPK-PRZ); października przekazane
        // 10.11, a listopad nie ma listy — przychód tylko w PIT-11 (010 R6).
        await Payroll.PpkPaidAsync(september, D(2026, 10, 10));
        var october = await Payroll.MonthAsync(company, 10);
        await Payroll.PpkPaidAsync(october, D(2026, 11, 10));
        // Naliczona, niezamknięta lista nie wchodzi do PIT-11.
        await Payroll.MonthAsync(company, 12, close: false);
        var entries = new[]
        {
            await Payroll.EntryAsync(september, employment),
            await Payroll.EntryAsync(october, employment),
        };

        Assert.Equal(1, await _kit.GenerateAsync(company));
        var form = await _kit.FormAsync(anna);
        var p = DocumentsKit.Positions(form);

        var income = entries.Sum(e => e.Gross + e.Amount("PPK-PRZ")) + entries[1].Amount("PPK-PR");
        var costs = entries.Sum(e => e.Amount("KUP"));
        Assert.Equal(entries[0].Amount("PPK-PR"), entries[1].Amount("PPK-PRZ"));
        Assert.True(entries[1].Amount("PPK-PR") > 0);
        Assert.Equal(income, p["P_29"]);
        Assert.Equal(costs, p["P_30"]);
        Assert.Equal(income - costs, p["P_31"]);
        Assert.Equal(entries.Sum(e => e.Amount("PIT")), p["P_33"]);
        Assert.Equal(
            entries.Sum(e => e.Amount("ZUS-E-P") + e.Amount("ZUS-R-P") + e.Amount("ZUS-C-P")),
            p["P_95"]
        );
        Assert.Equal(entries.Sum(e => e.Amount("ZDROW")), p["P_122"]);
        Assert.Equal(1m, p["P_28"]);
        Assert.Equal(1m, p["P_11"]);
        Assert.Equal(2m, p["P_121"]);
        Assert.False(p.ContainsKey("P_36"));
        Assert.False(p.ContainsKey("P_96"));
        Assert.False(p.ContainsKey("P_109"));

        var data = DocumentsKit.Data(form);
        Assert.Equal(2, data.GetProperty("entryCount").GetInt32());
        Assert.Equal(0, data.GetProperty("missing").GetArrayLength());
        var address = data.GetProperty("taxpayer").GetProperty("address");
        Assert.Equal("Lipowa", address.GetProperty("street").GetString());
        Assert.Equal("1", address.GetProperty("house").GetString());
        Assert.Equal("2", address.GetProperty("apartment").GetString());
        Assert.Matches("^\\d{4}$", data.GetProperty("taxOffice").GetProperty("code").GetString());
        Assert.Matches("^\\d{11}$", (string)form["pesel"]!);
        Assert.Equal("filing", form["purpose"]);
        Assert.Equal(1, form["version"]);
    }

    [Fact]
    public async Task Person_under_26_with_relief_goes_to_part_g_and_a_waiver_moves_income_to_row_2()
    {
        var company = await Payroll.CompanyAsync();
        var piotr = await _kit.PersonAsync(company, "Piotr", D(2004, 3, 1));
        var job = await Payroll.EmploymentAsync(
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
        var mandate = await Payroll.EmploymentAsync(
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
        var olaJob = await Payroll.EmploymentAsync(
            ola,
            "employment",
            D(2026, 1, 1),
            null,
            "monthly",
            6000m,
            EmploymentInsurances
        );
        await Payroll.DeclarationAsync(ola, "youthReliefWaiver", D(2026, 1, 1));
        var october = await Payroll.MonthAsync(company, 10);
        var octoberMandates = await Payroll.MonthAsync(company, 10, "mandate");
        var piotrJob = await Payroll.EntryAsync(october, job);
        var piotrMandate = await Payroll.EntryAsync(octoberMandates, mandate);
        var olaEntry = await Payroll.EntryAsync(october, olaJob);

        Assert.Equal(2, await _kit.GenerateAsync(company));
        var p = DocumentsKit.Positions(await _kit.FormAsync(piotr));
        var o = DocumentsKit.Positions(await _kit.FormAsync(ola));

        // Całość zwolniona (ulga dla młodych): bez wierszy E, przychody w G, składki od zwolnionego w poz. 97.
        Assert.Equal(2600.00m, piotrJob.Amount("ZWOL-MLODZI"));
        Assert.False(p.ContainsKey("P_29"));
        Assert.False(p.ContainsKey("P_36"));
        Assert.False(p.ContainsKey("P_58"));
        Assert.False(p.ContainsKey("P_62"));
        Assert.Equal(2600.00m, p["P_110"]);
        Assert.Equal(piotrMandate.Amount("ZWOL-MLODZI"), p["P_111"]);
        Assert.Equal(2600.00m + piotrMandate.Amount("ZWOL-MLODZI"), p["P_109"]);
        Assert.Equal(
            new[] { piotrJob, piotrMandate }.Sum(e =>
                e.Amount("ZUS-E-P") + e.Amount("ZUS-R-P") + e.Amount("ZUS-C-P")
            ),
            p["P_97"]
        );
        Assert.False(p.ContainsKey("P_95"));
        Assert.Equal(piotrJob.Amount("ZDROW") + piotrMandate.Amount("ZDROW"), p["P_122"]);

        // Rezygnacja z ulgi: zaliczka pobierana — wiersz 2 i składki w poz. 96.
        Assert.False(olaEntry.Amounts.ContainsKey("ZWOL-MLODZI"));
        Assert.False(o.ContainsKey("P_29"));
        Assert.Equal(olaEntry.Gross, o["P_36"]);
        Assert.Equal(olaEntry.Amount("KUP"), o["P_37"]);
        Assert.Equal(olaEntry.Gross - olaEntry.Amount("KUP"), o["P_38"]);
        Assert.Equal(olaEntry.Amount("PIT"), o["P_40"]);
        Assert.Equal(
            olaEntry.Amount("ZUS-E-P") + olaEntry.Amount("ZUS-R-P") + olaEntry.Amount("ZUS-C-P"),
            o["P_96"]
        );
        Assert.False(o.ContainsKey("P_109"));
    }

    [Fact]
    public async Task Mandate_and_specific_work_fill_rows_6_and_5_and_flat_tax_stays_out()
    {
        var company = await Payroll.CompanyAsync();
        var marek = await _kit.PersonAsync(company, "Marek", D(1985, 6, 1));
        var mandate = await Payroll.EmploymentAsync(
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
        var work = await Payroll.EmploymentAsync(
            ewa,
            "specific_work",
            D(2026, 10, 1),
            D(2026, 10, 30),
            "lumpSum",
            2000m,
            NoInsurances,
            "author50"
        );
        var student = await _kit.PersonAsync(company, "Student", D(1995, 1, 1));
        await Payroll.EmploymentAsync(
            student,
            "mandate",
            D(2026, 10, 1),
            D(2026, 10, 31),
            "lumpSum",
            150m,
            NoInsurances,
            "flat20"
        );
        var mandates = await Payroll.MonthAsync(company, 10, "mandate");
        var works = await Payroll.MonthAsync(company, 10, "specificWork");
        var marekEntry = await Payroll.EntryAsync(mandates, mandate);
        var ewaEntry = await Payroll.EntryAsync(works, work);

        Assert.Equal(2, await _kit.GenerateAsync(company));
        var m = DocumentsKit.Positions(await _kit.FormAsync(marek));
        var e = DocumentsKit.Positions(await _kit.FormAsync(ewa));

        Assert.Equal(3000.00m, m["P_58"]);
        Assert.Equal(marekEntry.Amount("KUP"), m["P_59"]);
        Assert.Equal(3000.00m - marekEntry.Amount("KUP"), m["P_60"]);
        Assert.Equal(marekEntry.Amount("PIT"), m["P_61"]);
        Assert.Equal(
            marekEntry.Amount("ZUS-E-P")
                + marekEntry.Amount("ZUS-R-P")
                + marekEntry.Amount("ZUS-C-P"),
            m["P_95"]
        );
        Assert.False(m.ContainsKey("P_28"));
        Assert.Equal(2000.00m, e["P_54"]);
        Assert.Equal(1000.00m, e["P_55"]);
        Assert.Equal(1000.00m, e["P_56"]);
        Assert.Equal(ewaEntry.Amount("PIT"), e["P_57"]);
        Assert.Equal(120m, e["P_57"]);
        Assert.False(e.ContainsKey("P_95"));

        // Ryczałt (umowa do 200 zł) — PIT-8AR, nie PIT-11.
        var noPayments = await Assert.ThrowsAsync<PostgresException>(() =>
            _kit.GenerateAsync(company, student)
        );
        Assert.Equal("U0001", noPayments.SqlState);
        Assert.Contains("noPayments", noPayments.Hint);
        var list = await _kit.RowsAsync(
            "kadry",
            "SELECT * FROM documents_app.tax_form_list($1, 2026)",
            company
        );
        Assert.Equal(2, list.Count);
        Assert.DoesNotContain(list, row => (Guid)row["employee_id"]! == student);

        var summary = await _kit.RowsAsync(
            "kadry",
            "SELECT * FROM documents_app.company_tax_summary($1, 2026)",
            company
        );
        Assert.Equal(12, summary.Count);
        var month = summary.Single(row => (int)row["month"]! == 10);
        Assert.Equal(marekEntry.Amount("PIT"), month["mandate_advances"]);
        Assert.Equal(120m, month["specific_work_advances"]);
        Assert.Equal(0m, month["employment_advances"]);
        Assert.Equal(18m, month["mandate_flat_tax"]);
        Assert.Equal(1, month["flat_tax_people"]);
        Assert.Equal(0m, summary.Single(row => (int)row["month"]! == 11)["advances_total"]);
    }

    [Fact]
    public async Task Unsent_form_is_replaced_and_after_sending_a_change_makes_a_correction()
    {
        var company = await Payroll.CompanyAsync();
        var jan = await _kit.PersonAsync(company, "Jan", D(1985, 1, 1), withPesel: false);
        var employment = await Payroll.EmploymentAsync(
            jan,
            "employment",
            D(2026, 1, 1),
            null,
            "monthly",
            7000m,
            EmploymentInsurances
        );
        var october = await Payroll.MonthAsync(company, 10);

        Assert.Equal(1, await _kit.GenerateAsync(company, jan));
        var first = await _kit.FormAsync(jan);
        Assert.Equal("pesel", DocumentsKit.Data(first).GetProperty("missing")[0].GetString());
        var incomplete = await Assert.ThrowsAsync<PostgresException>(() =>
            Payroll.KadryAsync<object>(
                "CALL documents_app.pit11_mark_sent($1, DATE '2026-11-10')",
                first["id"]
            )
        );
        Assert.Equal("missingData", incomplete.Hint);

        await Payroll.AdminAsync<object>(
            """
            UPDATE employees.employees
            SET pesel_enc = ext.pgp_sym_encrypt($2, $3), pesel_hmac = ext.hmac($2, $3, 'sha256')
            WHERE id = $1
            """,
            jan,
            FakeIds.Pesel(D(1985, 1, 1)),
            TestDb.DataKey
        );
        Assert.Equal(1, await _kit.GenerateAsync(company));
        Assert.Equal(0, await _kit.GenerateAsync(company));
        var replaced = await _kit.FormAsync(jan);
        Assert.Equal(first["id"], replaced["id"]);
        Assert.Equal(1, replaced["version"]);
        Assert.Equal(0, DocumentsKit.Data(replaced).GetProperty("missing").GetArrayLength());

        var future = await Assert.ThrowsAsync<PostgresException>(() =>
            Payroll.KadryAsync<object>(
                "CALL documents_app.pit11_mark_sent($1, current_date + 1)",
                replaced["id"]
            )
        );
        Assert.Contains("future", future.Hint);
        var badHow = await Assert.ThrowsAsync<PostgresException>(() =>
            Payroll.KadryAsync<object>(
                "CALL documents_app.pit11_mark_given($1, DATE '2026-11-12', 'fax')",
                replaced["id"]
            )
        );
        Assert.Contains("givenHow", badHow.Hint);
        await Payroll.KadryAsync<object>(
            "CALL documents_app.pit11_mark_given($1, DATE '2026-11-12', 'paper')",
            replaced["id"]
        );
        await Payroll.KadryAsync<object>(
            "CALL documents_app.pit11_mark_sent($1, DATE '2026-11-12')",
            replaced["id"]
        );
        var twice = await Assert.ThrowsAsync<PostgresException>(() =>
            Payroll.KadryAsync<object>(
                "CALL documents_app.pit11_mark_sent($1, DATE '2026-11-12')",
                replaced["id"]
            )
        );
        Assert.Equal("sent", twice.Hint);

        // Korekta października po wysłaniu: lista pokazuje nieaktualny formularz, generowanie dodaje wersję 2.
        var bonus = await Payroll.ComponentTypeAsync("addition", "fixed");
        var correction = await Payroll.PayrollAsync(
            company,
            "correction",
            D(2026, 10, 1),
            D(2026, 11, 10),
            october
        );
        await Payroll.ComponentAsync(employment, bonus, 500m, D(2026, 10, 1), D(2026, 10, 31));
        await Payroll.CalculateAsync(correction);
        await Payroll.CloseAsync(correction);
        var outdated = Assert.Single(
            await _kit.RowsAsync(
                "kadry",
                "SELECT * FROM documents_app.tax_form_list($1, 2026)",
                company
            )
        );
        Assert.True((bool)outdated["is_outdated"]!);

        Assert.Equal(1, await _kit.GenerateAsync(company));
        var corrected = await _kit.FormAsync(jan);
        Assert.Equal(2, corrected["version"]);
        Assert.Equal("correction", corrected["purpose"]);
        Assert.Equal(
            DocumentsKit.Positions(replaced)["P_29"] + 500m,
            DocumentsKit.Positions(corrected)["P_29"]
        );
        var listed = Assert.Single(
            await _kit.RowsAsync(
                "kadry",
                "SELECT * FROM documents_app.tax_form_list($1, 2026)",
                company
            )
        );
        Assert.False((bool)listed["is_outdated"]!);
        Assert.Equal(2, listed["version"]);
        var notLatest = await Assert.ThrowsAsync<PostgresException>(() =>
            Payroll.KadryAsync<object>(
                "CALL documents_app.pit11_mark_sent($1, DATE '2026-11-12')",
                replaced["id"]
            )
        );
        Assert.Equal("notLatest", notLatest.Hint);
        Assert.Equal(
            2,
            (
                await _kit.RowsAsync("kadry", "SELECT * FROM documents_app.my_tax_forms($1)", jan)
            ).Count
        );
    }

    [Fact]
    public async Task Payslip_and_wage_card_come_from_closed_lists_and_the_employee_sees_only_own_documents()
    {
        var company = await Payroll.CompanyAsync();
        var anna = await _kit.PersonAsync(company, "Anna", D(1990, 5, 10));
        var employment = await Payroll.EmploymentAsync(
            anna,
            "employment",
            D(2026, 1, 1),
            null,
            "monthly",
            6000m,
            EmploymentInsurances
        );
        var other = await _kit.PersonAsync(company, "Inny", D(1980, 1, 1));
        var otherJob = await Payroll.EmploymentAsync(
            other,
            "employment",
            D(2026, 1, 1),
            null,
            "monthly",
            5000m,
            EmploymentInsurances
        );
        var card = await Payroll.ComponentTypeAsync("deduction", "fixed", "voluntaryOther");
        var consent = await Payroll.ConsentAsync(anna, card, 120m, D(2026, 1, 1));
        await Payroll.ComponentAsync(employment, card, 120m, D(2026, 1, 1), consent: consent);
        var october = await Payroll.MonthAsync(company, 10);
        var november = await Payroll.MonthAsync(company, 11, close: false);
        var octoberEntry = await Payroll.EntryAsync(october, employment);
        var novemberEntry = await Payroll.EntryAsync(november, employment);
        var otherEntry = await Payroll.EntryAsync(october, otherJob);
        var login = await _kit.LinkedUserAsync(anna);

        var payslip = Assert.Single(
            await _kit.RowsAsync(
                "kadry",
                "SELECT * FROM documents_app.payslip($1)",
                octoberEntry.Id
            )
        );
        Assert.Equal(octoberEntry.Gross, payslip["gross"]);
        Assert.Equal(octoberEntry.Payout, payslip["payout"]);
        Assert.Equal(octoberEntry.Amount("PODST-PIT"), payslip["tax_base"]);
        Assert.Contains("\"ZASAD\"", (string)payslip["lines"]!);
        var notClosed = await Assert.ThrowsAsync<PostgresException>(() =>
            _kit.RowsAsync("kadry", "SELECT * FROM documents_app.payslip($1)", novemberEntry.Id)
        );
        Assert.Equal("notClosed", notClosed.Hint);
        var audited = await Payroll.AdminAsync<long>(
            "SELECT count(*) FROM platform.read_audit_entries WHERE resource = 'payslip' AND resource_id = $1",
            octoberEntry.Id.ToString()
        );
        Assert.Equal(1, audited);

        var wageCard = await _kit.RowsAsync(
            "kadry",
            "SELECT * FROM documents_app.wage_card($1, 2026)",
            anna
        );
        var row = Assert.Single(wageCard);
        Assert.Equal(octoberEntry.Amount("PIT"), row["pit"]);
        Assert.Equal(octoberEntry.Amount("ZDROW"), row["health"]);
        Assert.Equal(120m, row["deductions"]);
        Assert.Equal(
            octoberEntry.Amount("ZUS-E-PR")
                + octoberEntry.Amount("ZUS-R-PR")
                + octoberEntry.Amount("ZUS-W-PR")
                + octoberEntry.Amount("FP")
                + octoberEntry.Amount("FGSP"),
            row["employer_contributions"]
        );

        // Pracownik: swoje paski i dokument zgody bez wpisu w audycie odczytu, cudze i karta — 403.
        Assert.Single(
            await _kit.RowsAsync(login, "SELECT * FROM documents_app.my_payslips($1)", anna)
        );
        Assert.Single(
            await _kit.RowsAsync(login, "SELECT * FROM documents_app.payslip($1)", octoberEntry.Id)
        );
        Assert.Equal(
            1,
            await Payroll.AdminAsync<long>(
                "SELECT count(*) FROM platform.read_audit_entries WHERE resource = 'payslip' AND resource_id = $1",
                octoberEntry.Id.ToString()
            )
        );
        var document = Assert.Single(
            await _kit.RowsAsync(login, "SELECT * FROM documents_app.consent_document($1)", consent)
        );
        Assert.Equal(120m, document["monthly_amount"]);
        Assert.Equal("voluntaryOther", document["deduction_category"]);
        foreach (
            var (sql, arg) in new (string, object)[]
            {
                ("SELECT * FROM documents_app.payslip($1)", otherEntry.Id),
                ("SELECT * FROM documents_app.payslip($1)", Guid.NewGuid()),
                ("SELECT * FROM documents_app.my_payslips($1)", other),
                ("SELECT * FROM documents_app.wage_card($1, 2026)", anna),
                ("SELECT * FROM documents_app.tax_form_list($1, 2026)", company),
                ("SELECT * FROM documents_app.company_tax_summary($1, 2026)", company),
            }
        )
        {
            var forbidden = await Assert.ThrowsAsync<PostgresException>(() =>
                _kit.RowsAsync(login, sql, arg)
            );
            Assert.Equal("U0004", forbidden.SqlState);
        }

        var generate = await Assert.ThrowsAsync<PostgresException>(() =>
            _kit.GenerateAsync(company, login: login)
        );
        Assert.Equal("U0004", generate.SqlState);

        // PIT-11 pracownik widzi dopiero po przekazaniu albo wysłaniu.
        await _kit.GenerateAsync(company);
        var form = await _kit.FormAsync(anna);
        Assert.Empty(
            await _kit.RowsAsync(login, "SELECT * FROM documents_app.my_tax_forms($1)", anna)
        );
        var hidden = await Assert.ThrowsAsync<PostgresException>(() =>
            _kit.RowsAsync(login, "SELECT * FROM documents_app.tax_form_get($1)", form["id"])
        );
        Assert.Equal("U0002", hidden.SqlState);
        await Payroll.KadryAsync<object>(
            "CALL documents_app.pit11_mark_given($1, DATE '2026-11-12', 'portal')",
            form["id"]
        );
        Assert.Single(
            await _kit.RowsAsync(login, "SELECT * FROM documents_app.my_tax_forms($1)", anna)
        );
        Assert.Single(
            await _kit.RowsAsync(login, "SELECT * FROM documents_app.tax_form_get($1)", form["id"])
        );
        var otherForm = await _kit.FormAsync(other);
        var foreign = await Assert.ThrowsAsync<PostgresException>(() =>
            _kit.RowsAsync(login, "SELECT * FROM documents_app.tax_form_get($1)", otherForm["id"])
        );
        Assert.Equal("U0004", foreign.SqlState);
        Assert.Equal(
            1,
            await Payroll.AdminAsync<long>(
                "SELECT count(*) FROM platform.read_audit_entries WHERE resource = 'tax_form' AND resource_id = $1",
                form["id"]!.ToString()
            )
        );
    }
}
