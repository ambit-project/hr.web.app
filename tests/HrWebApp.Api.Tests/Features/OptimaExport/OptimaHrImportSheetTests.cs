using HrWebApp.Api.Features.OptimaExport;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;

namespace HrWebApp.Api.Tests.Features.OptimaExport;

public sealed class OptimaHrImportSheetTests
{
    [Fact]
    public void Sheet_has_optima_headers_in_order_and_keeps_pesel_as_text()
    {
        var file = OptimaHrImportSheet.Write([
            new Dictionary<string, object?>
            {
                ["Kod"] = "202611ABCDEFGH",
                ["Nazwisko"] = "Kowalska",
                ["Imie"] = "Anna",
                ["Pesel"] = "02270803628",
                ["Data_urodzenia"] = new DateOnly(2002, 7, 8),
            },
        ]);

        using var workbook = new HSSFWorkbook(new MemoryStream(file));
        var sheet = workbook.GetSheet("Dane_kadrowe");
        var header = sheet.GetRow(0);
        Assert.Equal(139, header.LastCellNum);
        Assert.Equal("Kod", header.GetCell(0).StringCellValue);
        Assert.Equal("Opis_pracownika", header.GetCell(138).StringCellValue);
        var row = sheet.GetRow(1);
        var pesel = row.GetCell(header.Cells.FindIndex(c => c.StringCellValue == "Pesel"));
        Assert.Equal(CellType.String, pesel.CellType);
        Assert.Equal("02270803628", pesel.StringCellValue);
        var birth = row.GetCell(header.Cells.FindIndex(c => c.StringCellValue == "Data_urodzenia"));
        Assert.Equal("2002-07-08", birth.StringCellValue);
        var rate = row.GetCell(header.Cells.FindIndex(c => c.StringCellValue == "Stawka"));
        Assert.Equal(CellType.Numeric, rate.CellType);
    }

    [Fact]
    public void Row_without_kod_or_with_unknown_column_is_a_programming_error()
    {
        Assert.Throws<ArgumentException>(() =>
            OptimaHrImportSheet.Write([
                new Dictionary<string, object?> { ["Nazwisko"] = "X", ["Imie"] = "Y" },
            ])
        );
        Assert.Throws<ArgumentException>(() =>
            OptimaHrImportSheet.Write([
                new Dictionary<string, object?>
                {
                    ["Kod"] = "A",
                    ["Nazwisko"] = "X",
                    ["Imie"] = "Y",
                    ["Rodzaj umowy"] = "?",
                },
            ])
        );
    }

    [Theory]
    [InlineData("02270803628", "K")]
    [InlineData("90051012375", "M")]
    [InlineData(null, null)]
    [InlineData("123", null)]
    public void Sex_comes_from_the_tenth_pesel_digit(string? pesel, string? expected) =>
        Assert.Equal(expected, OptimaEmployeeRow.Sex(pesel));
}
