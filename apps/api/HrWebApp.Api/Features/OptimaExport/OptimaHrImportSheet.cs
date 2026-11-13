using System.Globalization;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;

namespace HrWebApp.Api.Features.OptimaExport;

/// <summary>
/// Plik, z którego Comarch ERP Optima importuje pracowników (Narzędzia / Importy / Danych kadrowych / z arkusza MS
/// Excel; pomoc Optimy 2026.5): skoroszyt Excel 97-2003 (<c>.xls</c> — jedyny format w pomocy), jeden arkusz
/// <c>Dane_kadrowe</c>, 139 nagłówków w wierszu 1 w kolejności <c>kadry.xls</c> eksportowanego przez Optimę (układ jak
/// w ambit.es). Każda komórka to tekst — PESEL zachowuje zero na początku, data RRRR-MM-DD niezależnie od ustawień
/// regionalnych — poza pięcioma kolumnami, które Optima zapisuje jako liczby. Kolumna, której nikt nie wypełnia,
/// dostaje domyślną Optimy (<c>Archiwalny</c> 0, <c>Wydzial</c> FIRMA, <c>Waluta</c> PLN…). Optima łączy wiersz
/// z istniejącym pracownikiem po <c>Kod</c> (akronim, najwyżej 20 znaków).
/// </summary>
public static class OptimaHrImportSheet
{
    public const string SheetName = "Dane_kadrowe";
    public const string ContentType = "application/vnd.ms-excel";
    private const int CodeMaxLength = 20;

    /// <summary>Arkusz Excel 97-2003 kończy się na wierszu 65 536; jeden zajmuje nagłówek.</summary>
    public const int MaxRows = 65_535;

    /// <summary>Kolumna arkusza: nagłówek, czy Optima zapisuje ją jako liczbę, wartość domyślna Optimy.</summary>
    public sealed record Column(string Header, bool IsNumber, object? Default);

    private static Column Text(string header, string? fallback = null) =>
        new(header, false, fallback);

    private static Column Number(string header, decimal? fallback = null) =>
        new(header, true, fallback);

    public static readonly IReadOnlyList<Column> Columns =
    [
        Text("Kod"),
        Text("Nazwisko"),
        Text("Imie"),
        Text("Archiwalny", "0"),
        Text("Drugie_imie"),
        Text("Data_urodzenia"),
        Text("Miejsce_urodzenia"),
        Text("Imie_ojca"),
        Text("Imie_matki"),
        Text("Nazwisko_rodowe"),
        Text("Nazwisko_rodowe_matki"),
        Text("Plec"),
        Text("Pesel"),
        Text("NIP"),
        Text("Dowod_osobisty"),
        Text("Dowod_wydany_przez"),
        Text("Dowod_data_wydania"),
        Text("Dowod_data_waznosci"),
        Text("Paszport"),
        Text("Obywatelstwo"),
        Text("Kod_kraju_obywatelstwa"),
        Text("Wydzial", "FIRMA"),
        Text("Wydzial_adres_wezla", "1"),
        Text("RCP_Numer"),
        Text("RCP_OkresOd"),
        Text("RCP_OkresDo"),
        Text("Kraj", "Polska"),
        Text("Kod_kraju", "PL"),
        Text("Wojewodztwo"),
        Text("Powiat"),
        Text("Gmina"),
        Text("Miejscowosc"),
        Text("Ulica"),
        Text("Nr_domu"),
        Text("Nr_lokalu"),
        Text("Kod_pocztowy"),
        Text("Poczta"),
        Text("Rodzaj_miejscowosci", "0"),
        Text("Kod_gminy", "0"),
        Text("Telefon"),
        Text("Email"),
        Text("Kraj_zamieszkania", "Polska"),
        Text("Kod_kraju_zamieszkania", "PL"),
        Text("Wojewodztwo_zamieszkania"),
        Text("Powiat_zamieszkania"),
        Text("Gmina_zamieszkania"),
        Text("Miejscowosc_zamieszkania"),
        Text("Ulica_zamieszkania"),
        Text("Nr_domu_zamieszkania"),
        Text("Nr_lokalu_zamieszkania"),
        Text("Kod_pocztowy_zamieszkania"),
        Text("Poczta_zamieszkania"),
        Text("Kraj_korespondencja", "Polska"),
        Text("Kod_kraju_korespondencja", "PL"),
        Text("Wojewodztwo_korespondencja"),
        Text("Powiat_korespondencja"),
        Text("Gmina_korespondencja"),
        Text("Miejscowosc_korespondencja"),
        Text("Ulica_korespondencja"),
        Text("Nr_domu_korespondencja"),
        Text("Nr_lokalu_korespondencja"),
        Text("Kod_pocztowy_korespondencja"),
        Text("Poczta_korespondencja"),
        Text("Data_zatrudnienia"),
        Text("Data_zawarcia_umowy"),
        Text("Data_rozpoczecia_pracy"),
        Text("Data_zwolnienia"),
        Text("Rodzaj_zatrudnienia", "0"),
        Text("Rodzaj_umowy"),
        Text("Stanowisko"),
        Text("Licznik_wymiaru_etatu"),
        Text("Mianownik_wymiaru_etatu"),
        Text("Rodzaj_stawki", "1"),
        Text("Czy_stawka_minimalna", "0"),
        Number("Stawka", 0m),
        Text("Waluta", "PLN"),
        Text("ZestawienieCzasuPracy", "0"),
        Number("Procent_minimalnej", 100m),
        Text("Dobowa_norma_do_urlopu", "480"),
        Text("Limit_nadgodzin_w_minutach"),
        Text("Kod_zawodu"),
        Text("Kod_oddzialu_NFZ"),
        Text("Data_przyst_do_NFZ"),
        Text("Kod_wyksztalcenia", "0"),
        Text("Kod_wyksztalcenia_PFRON", "0"),
        Text("Wiek_emerytalny"),
        Text("Badania_okresowe"),
        Number("Koszty_uzyskania_mnoznik", 1m),
        Text("Bez_FP_ponizej_minimalnej", "0"),
        Text("US_akronim"),
        Text("US_nazwa"),
        Text("US_ekod"),
        Text("US_miejscowosc"),
        Text("US_ulica"),
        Text("US_nr_domu"),
        Text("US_kod_pocztowy"),
        Number("Ulga_podatkowa_licznik", 1m),
        Number("Ulga_podatkowa_mianownik", 1m),
        Text("Kod_tytulu_ubezpieczenia"),
        Text("Prawo_do_renty_i_emerytury", "0"),
        Text("Stopien_niepelnosprawnosci", "0"),
        Text("Niepelnosprawnosc_od"),
        Text("Niepelnosprawnosc_do"),
        Text("Ubezpieczenie_ZUS_od"),
        Text("Emerytalne", "0"),
        Text("Rentowe", "0"),
        Text("Chorobowe", "0"),
        Text("Wypadkowe", "0"),
        Text("Ubezpieczenie_zdrowotne_od"),
        Text("Emerytalne_dobr", "0"),
        Text("Emerytalne_dobr_od"),
        Text("Rentowe_dobr", "0"),
        Text("Rentowe_dobr_od"),
        Text("Chorobowe_dobr", "0"),
        Text("Chorobowe_dobr_od"),
        Text("Zdrowotne_dobr", "0"),
        Text("Zdrowotne_dobr_od"),
        Text("Praca_w_szczeg_warunk"),
        Text("Praca_w_szczeg_warunk_od"),
        Text("Praca_w_szczeg_warunk_do"),
        Text("Szczeg_warunki_FEP_kod", "0"),
        Text("Szczeg_warunki_FEP_od"),
        Text("Szczeg_warunki_FEP_do"),
        Text("PPK_przystapienie_od"),
        Text("PPK_rezygnacja_od"),
        Text("PPK_obnizenie_procent", "0"),
        Text("PPK_obnizenie_wniosek_data"),
        Text("PPK_skl_dodat_prac_procent", "0"),
        Text("PPK_skl_dodat_wniosek_data"),
        Text("PPK_ind_dodat_firma", "0"),
        Text("Nie_naliczac_FP", "0"),
        Text("Nie_naliczac_FP_od_daty"),
        Text("Nie_naliczac_FP_do_daty"),
        Text("Nie_naliczac_FGSP", "0"),
        Text("Nie_naliczac_FGSP_od_daty"),
        Text("Nie_naliczac_FGSP_do_daty"),
        Text("Kategoria"),
        Text("Opis_kategorii"),
        Text("Opis_pracownika"),
    ];

    private static readonly Dictionary<string, Column> ByHeader = Columns.ToDictionary(c =>
        c.Header
    );

    /// <summary>
    /// Skoroszyt dla wierszy (nagłówek → wartość). Tekst zapisuje się jak podany, kolumna liczbowa bierze
    /// <see cref="decimal"/>; pusta albo brakująca wartość — domyślna kolumny. Nieznany nagłówek, brak Kod / Nazwisko /
    /// Imie, Kod dłuższy niż 20 znaków albo więcej wierszy niż <see cref="MaxRows"/> to błąd programu (wiersze buduje
    /// API), nie danych — wyjątek.
    /// </summary>
    public static byte[] Write(IEnumerable<IReadOnlyDictionary<string, object?>> rows)
    {
        using var workbook = new HSSFWorkbook();
        var sheet = workbook.CreateSheet(SheetName);

        var header = sheet.CreateRow(0);
        for (var index = 0; index < Columns.Count; index++)
        {
            header.CreateCell(index, CellType.String).SetCellValue(Columns[index].Header);
        }

        var number = 0;
        foreach (var values in rows)
        {
            if (number == MaxRows)
            {
                throw new ArgumentException("Optima .xls holds at most 65,535 rows.");
            }
            foreach (var key in values.Keys.Where(key => !ByHeader.ContainsKey(key)))
            {
                throw new ArgumentException($"Optima HR sheet has no column '{key}'.");
            }
            if (!HasText(values, "Kod") || !HasText(values, "Nazwisko") || !HasText(values, "Imie"))
            {
                throw new ArgumentException("Every row needs Kod, Nazwisko and Imie.");
            }
            if (TextOf(values["Kod"]!).Length > CodeMaxLength)
            {
                throw new ArgumentException("Optima Kod holds at most 20 characters.");
            }

            var row = sheet.CreateRow(++number);
            for (var index = 0; index < Columns.Count; index++)
            {
                var column = Columns[index];
                var value =
                    values.GetValueOrDefault(column.Header) is { } given && !IsBlank(given)
                        ? given
                        : column.Default;
                switch (value)
                {
                    case null:
                        continue;
                    case decimal amount when column.IsNumber:
                        row.CreateCell(index, CellType.Numeric).SetCellValue((double)amount);
                        break;
                    default:
                        row.CreateCell(index, CellType.String).SetCellValue(TextOf(value));
                        break;
                }
            }
        }

        using var stream = new MemoryStream();
        workbook.Write(stream, leaveOpen: true);
        return stream.ToArray();
    }

    private static bool HasText(IReadOnlyDictionary<string, object?> values, string header) =>
        values.GetValueOrDefault(header) is { } value && !IsBlank(value);

    private static bool IsBlank(object value) =>
        value is string text && string.IsNullOrWhiteSpace(text);

    private static string TextOf(object value) =>
        value switch
        {
            DateOnly day => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            bool flag => flag ? "1" : "0",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "",
        };
}
