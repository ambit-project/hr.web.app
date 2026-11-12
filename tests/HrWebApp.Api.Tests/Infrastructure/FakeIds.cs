namespace HrWebApp.Api.Tests.Infrastructure;

/// <summary>Losowe, poprawne sumą kontrolną NIP i REGON — testy nie używają numerów prawdziwych firm.</summary>
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

    /// <summary>Ten sam numer z ostatnią cyfrą zmienioną — zła suma kontrolna.</summary>
    public static string Broken(string id) => id[..^1] + (char)('0' + (id[^1] - '0' + 1) % 10);
}
