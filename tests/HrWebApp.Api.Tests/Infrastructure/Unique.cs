namespace HrWebApp.Api.Tests.Infrastructure;

public static class Unique
{
    /// <summary>Unikalna nazwa — testy dzielą jedną bazę i nie sprzątają po sobie.</summary>
    public static string Name(string prefix) =>
        $"{prefix} {Guid.NewGuid():N}"[..Math.Min(prefix.Length + 13, 200)];
}
