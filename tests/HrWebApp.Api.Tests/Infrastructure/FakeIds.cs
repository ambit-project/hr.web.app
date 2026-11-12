using System.Globalization;
using System.Numerics;

namespace HrWebApp.Api.Tests.Infrastructure;

/// <summary>Losowe, poprawne sumą kontrolną NIP, REGON, PESEL i IBAN — testy nie używają prawdziwych numerów.</summary>
public static class FakeIds
{
    private static readonly int[] NipWeights = [6, 5, 7, 2, 3, 4, 5, 6, 7];
    private static readonly int[] Regon9Weights = [8, 9, 2, 3, 4, 5, 6, 7];

    public static string Nip()
    {
        while (true)
        {
            var digits = Enumerable.Range(0, 9).Select(_ => Random.Shared.Next(10)).ToArray();
            var check = digits.Select((d, i) => d * NipWeights[i]).Sum() % 11;
            if (check != 10)
            {
                return string.Concat(digits) + check;
            }
        }
    }

    public static string Regon()
    {
        var digits = Enumerable.Range(0, 8).Select(_ => Random.Shared.Next(10)).ToArray();
        var check = digits.Select((d, i) => d * Regon9Weights[i]).Sum() % 11 % 10;
        return string.Concat(digits) + check;
    }

    private static readonly int[] PeselWeights = [1, 3, 7, 9, 1, 3, 7, 9, 1, 3];

    /// <summary>PESEL z datą urodzenia i losowym numerem serii (miesiąc +20 dla lat 2000–2099).</summary>
    public static string Pesel(DateOnly birthDate)
    {
        var month = birthDate.Month + (birthDate.Year >= 2000 ? 20 : 0);
        var prefix =
            $"{birthDate.Year % 100:D2}{month:D2}{birthDate.Day:D2}{Random.Shared.Next(10000):D4}";
        var sum = prefix.Select((c, i) => (c - '0') * PeselWeights[i]).Sum();
        return prefix + (10 - sum % 10) % 10;
    }

    /// <summary>Polski IBAN (PL + 26 cyfr) z poprawnymi cyframi kontrolnymi.</summary>
    public static string Iban()
    {
        var bban = string.Concat(Enumerable.Range(0, 24).Select(_ => Random.Shared.Next(10)));
        var check =
            98 - (int)(BigInteger.Parse(bban + "252100", CultureInfo.InvariantCulture) % 97);
        return $"PL{check:D2}{bban}";
    }

    /// <summary>Ten sam numer z ostatnią cyfrą zmienioną — zła suma kontrolna.</summary>
    public static string Broken(string id) => id[..^1] + (char)('0' + (id[^1] - '0' + 1) % 10);
}
