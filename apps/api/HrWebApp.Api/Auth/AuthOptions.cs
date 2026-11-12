using System.ComponentModel.DataAnnotations;
using JetBrains.Annotations;

namespace HrWebApp.Api.Auth;

/// <summary>Ustawienia bezpieczeństwa logowania — tylko ze zmiennych środowiskowych (sekcja Auth), nigdy z UI.</summary>
[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.WithMembers)]
public sealed class AuthOptions
{
    public const string Section = "Auth";

    /// <summary>Auth__SessionHours: ważność sesji od zalogowania.</summary>
    [Range(1, 168)]
    public int SessionHours { get; init; } = 12;

    /// <summary>Auth__MaxFailedAttempts: nieudane próby z rzędu do blokady konta.</summary>
    [Range(1, 100)]
    public int MaxFailedAttempts { get; init; } = 5;

    /// <summary>Auth__LockoutMinutes: czas blokady konta.</summary>
    [Range(1, 1440)]
    public int LockoutMinutes { get; init; } = 15;

    /// <summary>Auth__DevLogin: logowanie jednym kliknięciem na rolę; działa tylko w Development.</summary>
    public bool DevLogin { get; init; }

    public BootstrapAdminOptions BootstrapAdmin { get; init; } = new();
}

/// <summary>Auth__BootstrapAdmin__*: pierwszy Administrator, zakładany przy starcie, gdy żadnego nie ma.</summary>
[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.WithMembers)]
public sealed class BootstrapAdminOptions
{
    public string? Login { get; init; }
    public string? Email { get; init; }
    public string DisplayName { get; init; } = "Administrator";
    public string? Password { get; init; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Login)
        && !string.IsNullOrWhiteSpace(Email)
        && !string.IsNullOrWhiteSpace(Password);
}
