using System.Globalization;
using HrWebApp.Api.Common.Pdf;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace HrWebApp.Api.Features.Documents;

/// <summary>
/// Pasek wypłaty jako PDF (011 R6): ten sam układ co strona do wydruku (<c>PayslipPrintPage.tsx</c>) — nagłówek,
/// pracodawca, pracownik, umowa, lista, pozycje w grupach z sumami, podstawy, brutto / netto / do wypłaty. Nazwy
/// pozycji silnika i grupy — jak w <c>apps/web/src/i18n/pl.ts</c> (<c>payroll.codes</c>, <c>payroll.payslip.groups</c>);
/// składnik płacowy ma własną nazwę. A4, Noto Sans.
/// </summary>
public static class PayslipPdf
{
    private static readonly CultureInfo Polish = CultureInfo.GetCultureInfo("pl-PL");
    private static readonly Color Muted = new(0x6B, 0x72, 0x80);
    private static readonly Color Line = new(0xD1, 0xD5, 0xDB);
    private static readonly Color Shade = new(0xF3, 0xF4, 0xF6);

    private static readonly IReadOnlyDictionary<string, string> CodeNames = new Dictionary<
        string,
        string
    >
    {
        ["ZASAD"] = "Wynagrodzenie zasadnicze",
        ["ZASAD-CH"] = "Pomniejszenie za chorobę (1/30)",
        ["ZASAD-NB"] = "Pomniejszenie za nieobecność niepłatną",
        ["SW50"] = "Zwolnienie — siła wyższa (50%)",
        ["CHOR"] = "Wynagrodzenie chorobowe",
        ["ZAS-CH"] = "Zasiłek chorobowy (płatnik)",
        ["ZAS-ZUS"] = "Zasiłek chorobowy — wypłaca ZUS",
        ["URLOP-ZM"] = "Urlop — składniki zmienne",
        ["NADG50"] = "Nadgodziny z dodatkiem 50%",
        ["NADG100"] = "Nadgodziny z dodatkiem 100%",
        ["ZLEC"] = "Wynagrodzenie z umowy zlecenia",
        ["DZIELO"] = "Wynagrodzenie z umowy o dzieło",
        ["EKWIW"] = "Ekwiwalent za niewykorzystany urlop",
        ["PODST-ZUS"] = "Podstawa składek społecznych",
        ["PODST-CHOR"] = "Podstawa składki chorobowej",
        ["ZUS-E-P"] = "Składka emerytalna (pracownik)",
        ["ZUS-R-P"] = "Składka rentowa (pracownik)",
        ["ZUS-C-P"] = "Składka chorobowa",
        ["ZUS-E-PR"] = "Składka emerytalna (pracodawca)",
        ["ZUS-R-PR"] = "Składka rentowa (pracodawca)",
        ["ZUS-W-PR"] = "Składka wypadkowa",
        ["FP"] = "Fundusz Pracy i Fundusz Solidarnościowy",
        ["FGSP"] = "Fundusz Gwarantowanych Świadczeń Pracowniczych",
        ["PPK-P"] = "PPK — wpłata podstawowa pracownika",
        ["PPK-PD"] = "PPK — wpłata dodatkowa pracownika",
        ["PPK-PR"] = "PPK — wpłata podstawowa pracodawcy",
        ["PPK-PRD"] = "PPK — wpłata dodatkowa pracodawcy",
        ["PPK-PRZ"] = "Przychód z wpłat pracodawcy PPK (przekazanych w miesiącu wypłaty)",
        ["ZDROW"] = "Składka zdrowotna",
        ["ZWOL-MLODZI"] = "Przychód zwolniony z PIT (ulga)",
        ["KUP"] = "Koszty uzyskania przychodu",
        ["KUP50"] = "Koszty uzyskania przychodu 50%",
        ["PODST-PIT"] = "Podstawa opodatkowania",
        ["PIT-ZMN"] = "Kwota zmniejszająca podatek",
        ["PIT"] = "Zaliczka na podatek dochodowy",
        ["PIT-RYCZ"] = "Podatek zryczałtowany",
        ["ALIM"] = "Zajęcie — alimenty",
        ["KOMOR"] = "Zajęcie — egzekucja",
    };

    private static readonly string[] TaxCodes =
    [
        "KUP",
        "KUP50",
        "PODST-PIT",
        "ZWOL-MLODZI",
        "PIT-ZMN",
        "PIT",
        "PIT-RYCZ",
    ];

    private static readonly (string Key, string Name)[] Groups =
    [
        ("income", "Przychody"),
        ("contributions", "Składki pracownika"),
        ("tax", "Podatek"),
        ("ppk", "PPK"),
        ("deductions", "Potrącenia"),
        ("employer", "Pracodawca"),
        ("info", "Informacyjne"),
    ];

    private static readonly Dictionary<string, string> ContractKinds = new()
    {
        ["employment"] = "umowa o pracę",
        ["mandate"] = "umowa zlecenie",
        ["specific_work"] = "umowa o dzieło",
    };

    private static readonly Dictionary<string, string> PayrollKinds = new()
    {
        ["employment"] = "Umowy o pracę",
        ["mandate"] = "Umowy zlecenia",
        ["specificWork"] = "Umowy o dzieło",
        ["correction"] = "Korekta",
    };

    /// <summary>Grupa pozycji jak <c>lineGroup</c> na froncie.</summary>
    private static string GroupOf(string code, string side) =>
        TaxCodes.Contains(code) ? "tax"
        : code.StartsWith("PPK-", StringComparison.Ordinal) ? "ppk"
        : side switch
        {
            "income" => "income",
            "employee" => "contributions",
            "deduction" => "deductions",
            "employer" => "employer",
            _ => "info",
        };

    /// <summary>
    /// Suma grupy jak na froncie: PPK i informacyjne bez sumy; podatek liczy tylko zaliczkę i ryczałt.
    /// </summary>
    private static decimal? GroupTotal(string group, IReadOnlyList<GetPayslip.Line> lines) =>
        group switch
        {
            "ppk" or "info" => null,
            "tax" => lines.Where(l => l.Code is "PIT" or "PIT-RYCZ").Sum(l => l.Amount),
            _ => lines.Sum(l => l.Amount),
        };

    public static string FileName(GetPayslip.Response payslip) =>
        $"pasek-{payslip.PayrollNumber.Replace('/', '-')}-{payslip.PaymentDate:yyyy-MM-dd}.pdf";

    public static byte[] Build(GetPayslip.Response payslip)
    {
        PdfFonts.EnsureRegistered();
        var document = new Document
        {
            Info =
            {
                Title =
                    $"Pasek wypłaty {payslip.PayrollNumber} — {payslip.LastName} {payslip.FirstName}",
            },
        };
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = PdfFonts.Family;
        normal.Font.Size = 8.5;

        var section = document.AddSection();
        var page = section.PageSetup;
        page.PageFormat = PageFormat.A4;
        page.TopMargin = page.BottomMargin = Unit.FromCentimeter(1.5);
        page.LeftMargin = page.RightMargin = Unit.FromCentimeter(1.5);

        var title = section.AddParagraph();
        title.AddFormattedText("Pasek wypłaty", new Font { Size = 15, Bold = true });
        title.AddTab();
        title.Format.TabStops.AddTabStop(Unit.FromCentimeter(18), TabAlignment.Right);
        title.AddText(
            $"Okres {payslip.PeriodFrom:MM.yyyy} · Data wypłaty {payslip.PaymentDate:yyyy-MM-dd}"
        );
        title.Format.SpaceAfter = Unit.FromPoint(8);

        var header = Table(section, [9, 9]);
        HeaderRow(
            header,
            (
                "Pracodawca",
                payslip.CompanyName,
                $"NIP {payslip.CompanyNip} · {payslip.CompanyAddress}"
            ),
            ("Pracownik", $"{payslip.LastName} {payslip.FirstName}", payslip.EmployeeNumber)
        );
        HeaderRow(
            header,
            (
                "Umowa",
                payslip.EmploymentNumber,
                ContractKinds.GetValueOrDefault(payslip.ContractKind, payslip.ContractKind)
            ),
            (
                "Lista płac",
                payslip.PayrollNumber,
                PayrollKinds.GetValueOrDefault(payslip.PayrollKind, payslip.PayrollKind)
            )
        );
        section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(4);

        var lines = Table(section, [2.4, 9.6, 2.4, 1.4, 2.2], 2, 3, 4);
        var head = lines.AddRow();
        head.HeadingFormat = true;
        head.Shading.Color = Shade;
        head.Format.Font.Bold = true;
        Cells(head, "Kod", "Pozycja", "Podstawa", "Stawka", "Kwota");
        foreach (var (key, name) in Groups)
        {
            var inGroup = payslip
                .Lines.Where(l => GroupOf(l.Code, l.Side) == key)
                .OrderBy(l => l.Seq)
                .ToList();
            if (inGroup.Count == 0)
            {
                continue;
            }
            var groupRow = lines.AddRow();
            groupRow.Cells[0].MergeRight = 4;
            groupRow.Cells[0].AddParagraph(name).Format.Font.Bold = true;
            groupRow.Shading.Color = Shade;
            foreach (var line in inGroup)
            {
                Cells(
                    lines.AddRow(),
                    line.Code,
                    CodeNames.GetValueOrDefault(line.Code) ?? line.Name ?? line.Code,
                    line.Base is { } amount ? Money(amount) : "",
                    line.Rate is { } rate ? (rate * 100).ToString("0.####", Polish) + "%" : "",
                    Money(line.Amount)
                );
            }
            if (GroupTotal(key, inGroup) is { } total)
            {
                var totalRow = lines.AddRow();
                totalRow.Cells[0].MergeRight = 3;
                totalRow.Cells[0].AddParagraph("Razem").Format.Alignment = ParagraphAlignment.Right;
                totalRow.Cells[4].AddParagraph(Money(total));
                totalRow.Format.Font.Bold = true;
            }
        }
        section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(4);

        var summary = Table(section, [6.5, 2.5, 0.5, 5.5, 3], 1, 4);
        var bases = new[]
        {
            ("Podstawa składek ZUS", payslip.ZusBase),
            ("Podstawa składki zdrowotnej", payslip.HealthBase),
            ("Koszty uzyskania przychodu", payslip.Costs),
            ("Podstawa opodatkowania", payslip.TaxBase),
        };
        var results = new[]
        {
            ("Przychód brutto", payslip.Gross),
            ("Netto", payslip.Net),
            ("Do wypłaty", payslip.Payout),
        };
        for (var index = 0; index < bases.Length; index++)
        {
            var row = summary.AddRow();
            row.Borders.Visible = false;
            row.Cells[0].AddParagraph(bases[index].Item1);
            row.Cells[1].AddParagraph(Money(bases[index].Item2));
            if (index < results.Length)
            {
                row.Cells[3].AddParagraph(results[index].Item1).Format.Font.Bold = true;
                var value = row.Cells[4].AddParagraph(Money(results[index].Item2) + " zł");
                value.Format.Font.Bold = true;
                value.Format.Font.Size = 10;
            }
        }

        var footer = section.AddParagraph(
            "Pasek z zamkniętej listy płac. Pozycje pracodawcy nie pomniejszają wypłaty."
        );
        footer.Format.SpaceBefore = Unit.FromPoint(10);
        footer.Format.Font.Size = 7;
        footer.Format.Font.Color = Muted;

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream);
        return stream.ToArray();
    }

    private static string Money(decimal amount) => amount.ToString("N2", Polish);

    /// <summary>Tabela o kolumnach szerokości w cm; <paramref name="right"/> — kolumny kwot wyrównane do prawej.</summary>
    private static Table Table(Section section, double[] widthsCm, params int[] right)
    {
        var table = section.AddTable();
        table.Borders.Color = Line;
        table.Borders.Width = 0.5;
        table.TopPadding = table.BottomPadding = Unit.FromPoint(1.5);
        table.LeftPadding = table.RightPadding = Unit.FromPoint(3);
        for (var index = 0; index < widthsCm.Length; index++)
        {
            var column = table.AddColumn(Unit.FromCentimeter(widthsCm[index]));
            if (right.Contains(index))
            {
                column.Format.Alignment = ParagraphAlignment.Right;
            }
        }
        return table;
    }

    private static void HeaderRow(
        Table table,
        params (string Label, string Value, string Detail)[] blocks
    )
    {
        var row = table.AddRow();
        for (var index = 0; index < blocks.Length; index++)
        {
            var (label, value, detail) = blocks[index];
            var cell = row.Cells[index];
            var caption = cell.AddParagraph(label);
            caption.Format.Font.Size = 6.5;
            caption.Format.Font.Color = Muted;
            cell.AddParagraph(value).Format.Font.Bold = true;
            cell.AddParagraph(detail);
        }
    }

    private static void Cells(Row row, params string[] values)
    {
        for (var index = 0; index < values.Length; index++)
        {
            row.Cells[index].AddParagraph(values[index]);
        }
    }
}
