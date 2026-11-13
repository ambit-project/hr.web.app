namespace HrWebApp.Api.Features.OptimaExport;

/// <summary>
/// Optima zapisuje kraj nazwą (<c>Kraj</c> „Polska” obok <c>Kod_kraju</c> PL), a kartoteka trzyma kod ISO. Kraje
/// najczęstsze w kadrach (lista jak w ambit.es); inny zostaje kodem, który import zachowuje.
/// </summary>
public static class OptimaCountries
{
    private static readonly Dictionary<string, string> Words = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PL"] = "Polska",
        ["UA"] = "Ukraina",
        ["BY"] = "Białoruś",
        ["RU"] = "Rosja",
        ["MD"] = "Mołdawia",
        ["GE"] = "Gruzja",
        ["AM"] = "Armenia",
        ["AZ"] = "Azerbejdżan",
        ["KZ"] = "Kazachstan",
        ["UZ"] = "Uzbekistan",
        ["KG"] = "Kirgistan",
        ["TJ"] = "Tadżykistan",
        ["TR"] = "Turcja",
        ["IN"] = "Indie",
        ["NP"] = "Nepal",
        ["BD"] = "Bangladesz",
        ["PH"] = "Filipiny",
        ["VN"] = "Wietnam",
        ["ID"] = "Indonezja",
        ["CO"] = "Kolumbia",
        ["DE"] = "Niemcy",
        ["CZ"] = "Czechy",
        ["SK"] = "Słowacja",
        ["LT"] = "Litwa",
        ["LV"] = "Łotwa",
        ["EE"] = "Estonia",
        ["RO"] = "Rumunia",
        ["BG"] = "Bułgaria",
        ["HU"] = "Węgry",
        ["NL"] = "Holandia",
        ["BE"] = "Belgia",
        ["FR"] = "Francja",
        ["IT"] = "Włochy",
        ["ES"] = "Hiszpania",
        ["PT"] = "Portugalia",
        ["GB"] = "Wielka Brytania",
        ["IE"] = "Irlandia",
        ["NO"] = "Norwegia",
        ["SE"] = "Szwecja",
        ["DK"] = "Dania",
        ["FI"] = "Finlandia",
        ["AT"] = "Austria",
        ["CH"] = "Szwajcaria",
        ["US"] = "Stany Zjednoczone",
    };

    /// <summary>„Polska” dla PL; nieznany kod zostaje kodem.</summary>
    public static string? Name(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null
        : Words.TryGetValue(code.Trim(), out var name) ? name
        : code.Trim().ToUpperInvariant();
}
