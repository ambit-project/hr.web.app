using Microsoft.AspNetCore.Authentication;

namespace HrWebApp.Api.Auth;

public static class AuthSetup
{
    public static IServiceCollection AddSessionAuth(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services
            .AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddAuthentication(SessionAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(
                SessionAuthenticationHandler.SchemeName,
                null
            );

        // Sesji wymaga grupa /api (RequireAuthorization w Program.cs), nie polityka globalna: nieznana trasa ma
        // dawać 404, a nie 401 (np. /api/auth/dev-login poza Development).
        services.AddAuthorization();

        services.AddHostedService<BootstrapAdminService>();
        return services;
    }
}
