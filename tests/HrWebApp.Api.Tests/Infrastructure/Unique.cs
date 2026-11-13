namespace HrWebApp.Api.Tests.Infrastructure;

public static class Unique
{
    /// <summary>Unikalna nazwa — testy dzielą jedną bazę i nie sprzątają po sobie.</summary>
    public static string Name(string prefix) =>
        $"{prefix} {Guid.NewGuid():N}"[..Math.Min(prefix.Length + 13, 200)];

    public static string Login() => $"u{Guid.NewGuid():N}"[..20];

    /// <summary>Unikalny kod słownikowy (wielkie litery i cyfry, 12 znaków).</summary>
    public static string Code() => $"T{Guid.NewGuid():N}"[..12].ToUpperInvariant();
}
