using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace HrWebApp.Api.Features.Documents;

/// <summary>
/// XML PIT-11(29) według schematu MF 13535 (<c>http://crd.gov.pl/wzor/2024/10/15/13535/schemat.xsd</c>,
/// wersjaSchemy 4-0E, wariant 29). Kolejność elementów jak w XSD; pozycje szczegółowe idą w kolejności numerów
/// (P_11 … P_122), bo w schemacie są ułożone rosnąco. Kwoty z groszami z kropką, zaliczki i znaczniki bez części
/// ułamkowej. Kopia schematu z importami: tests/HrWebApp.Api.Tests/Fixtures/pit11-29 (test waliduje wynik).
/// </summary>
public static class Pit11Xml
{
    public const string Namespace = "http://crd.gov.pl/wzor/2024/10/15/13535/";

    private static readonly XNamespace Tns = Namespace;

    public static string Build(GetTaxForm.Response form)
    {
        var data = form.Data;
        var address = data.Taxpayer.Address!;
        var document = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(
                Tns + "Deklaracja",
                new XAttribute(XNamespace.Xmlns + "tns", Namespace),
                new XElement(
                    Tns + "Naglowek",
                    new XElement(
                        Tns + "KodFormularza",
                        new XAttribute("kodSystemowy", "PIT-11 (29)"),
                        new XAttribute("kodPodatku", "PIT"),
                        new XAttribute("rodzajZobowiazania", "Z"),
                        new XAttribute("wersjaSchemy", "4-0E"),
                        "PIT-11"
                    ),
                    new XElement(Tns + "WariantFormularza", "29"),
                    new XElement(
                        Tns + "CelZlozenia",
                        new XAttribute("poz", "P_7"),
                        form.Purpose == "correction" ? "2" : "1"
                    ),
                    new XElement(Tns + "Rok", form.Year.ToString(CultureInfo.InvariantCulture)),
                    new XElement(Tns + "KodUrzedu", data.TaxOffice!.Code)
                ),
                new XElement(
                    Tns + "Podmiot1",
                    new XAttribute("rola", "Płatnik/Składający"),
                    new XElement(
                        Tns + "OsobaNiefizyczna",
                        new XElement(Tns + "NIP", data.Payer.Nip),
                        new XElement(Tns + "PelnaNazwa", data.Payer.Name)
                    )
                ),
                new XElement(
                    Tns + "Podmiot2",
                    new XAttribute("rola", "Podatnik"),
                    new XElement(
                        Tns + "OsobaFizyczna",
                        new XElement(Tns + "PESEL", form.Pesel),
                        new XElement(Tns + "ImiePierwsze", data.Taxpayer.FirstName),
                        new XElement(Tns + "Nazwisko", data.Taxpayer.LastName),
                        new XElement(
                            Tns + "DataUrodzenia",
                            data.Taxpayer.BirthDate.ToString(
                                "yyyy-MM-dd",
                                CultureInfo.InvariantCulture
                            )
                        )
                    ),
                    new XElement(
                        Tns + "AdresZamieszkania",
                        new XAttribute("rodzajAdresu", "RAD"),
                        Field("KodKraju", "P_19A", address.Country),
                        Field("Ulica", "P_23", address.Street),
                        Field("NrDomu", "P_24", address.House),
                        Field("NrLokalu", "P_25", address.Apartment),
                        Field("Miejscowosc", "P_26", address.City),
                        Field("KodPocztowy", "P_27", address.PostalCode)
                    )
                ),
                new XElement(
                    Tns + "PozycjeSzczegolowe",
                    data.Positions.OrderBy(p => Number(p.Key))
                        .Select(p => new XElement(Tns + p.Key, Amount(p.Value)))
                ),
                new XElement(Tns + "Pouczenie", "1")
            )
        );

        var builder = new StringBuilder();
        using (
            var writer = XmlWriter.Create(
                new Utf8StringWriter(builder),
                new XmlWriterSettings { Indent = true, Encoding = Encoding.UTF8 }
            )
        )
        {
            document.Save(writer);
        }

        return builder.ToString();
    }

    private static XElement? Field(string name, string position, string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : new XElement(Tns + name, new XAttribute("poz", position), value);

    private static int Number(string position) =>
        int.Parse(position.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture);

    private static string Amount(decimal value) =>
        value == decimal.Truncate(value)
            ? value.ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>StringWriter deklarujący UTF-8 — inaczej nagłówek XML mówiłby utf-16.</summary>
    private sealed class Utf8StringWriter(StringBuilder builder)
        : StringWriter(builder, CultureInfo.InvariantCulture)
    {
        public override Encoding Encoding => Encoding.UTF8;
    }
}
