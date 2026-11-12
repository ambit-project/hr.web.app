using System.ComponentModel.DataAnnotations;
using JetBrains.Annotations;

namespace HrWebApp.Api.Database;

/// <summary>Ochrona danych osobowych (ADR-0009) — tylko ze zmiennych środowiskowych, nigdy z UI ani z repo.</summary>
[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.WithMembers)]
public sealed class DataOptions
{
    public const string Section = "Data";

    /// <summary>
    /// Data__EncryptionKey: klucz szyfrowania PESEL, dokumentu i rachunku oraz HMAC PESEL. Utrata klucza = utrata tych
    /// danych; zmiana wymaga przeszyfrowania.
    /// </summary>
    [Required]
    [MinLength(32)]
    public string EncryptionKey { get; init; } = string.Empty;
}
