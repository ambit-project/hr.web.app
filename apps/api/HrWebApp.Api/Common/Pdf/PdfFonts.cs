using PdfSharp.Fonts;

namespace HrWebApp.Api.Common.Pdf;

/// <summary>
/// Czcionki osadzone w assembly: Noto Sans zwykła i pogrubiona (polskie znaki). PDFsharp w wersji Core nie widzi
/// czcionek systemu na Linuksie, więc każda rodzina, o którą poprosi MigraDoc, rozwiązuje się do Noto Sans; kursywa
/// jest symulowana.
/// </summary>
public sealed class PdfFonts : IFontResolver
{
    public const string Family = "Noto Sans";

    private const string Regular = "NotoSans-Regular";
    private const string Bold = "NotoSans-Bold";

    private static readonly Lock Gate = new();
    private static bool _registered;

    /// <summary>
    /// <see cref="GlobalFontSettings.FontResolver"/> ustawia się raz na proces; każdy generator woła to przed rysowaniem.
    /// </summary>
    public static void EnsureRegistered()
    {
        lock (Gate)
        {
            if (_registered)
            {
                return;
            }
            GlobalFontSettings.FontResolver ??= new PdfFonts();
            _registered = true;
        }
    }

    public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) =>
        new(bold ? Bold : Regular, false, italic);

    public byte[]? GetFont(string faceName)
    {
        using var stream = typeof(PdfFonts).Assembly.GetManifestResourceStream(
            $"Fonts.{faceName}.ttf"
        );
        if (stream is null)
        {
            return null;
        }
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
