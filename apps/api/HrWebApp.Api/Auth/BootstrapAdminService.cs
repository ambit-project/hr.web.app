using HrWebApp.Api.Database;
using Microsoft.Extensions.Options;
using Npgsql;

namespace HrWebApp.Api.Auth;

/// <summary>Zakłada pierwszego Administratora z Auth__BootstrapAdmin__*, gdy w bazie nie ma żadnego (R10).</summary>
public sealed partial class BootstrapAdminService(
    NpgsqlDataSource dataSource,
    IOptions<AuthOptions> options,
    ILogger<BootstrapAdminService> logger
) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var admin = options.Value.BootstrapAdmin;
        if (!admin.IsConfigured)
        {
            return;
        }

        await using var command = dataSource.CreateCommand(
            "CALL platform_app.bootstrap_admin($1, $2, $3, $4, NULL)"
        );
        command.Parameters.Add(Db.Param(admin.Login));
        command.Parameters.Add(Db.Param(admin.Email));
        command.Parameters.Add(Db.Param(admin.DisplayName));
        command.Parameters.Add(Db.Param(admin.Password));
        if (await command.ExecuteScalarAsync(cancellationToken) is true)
        {
            LogCreated(logger, admin.Login!);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Utworzono pierwszego Administratora {Login} z Auth__BootstrapAdmin."
    )]
    private static partial void LogCreated(ILogger logger, string login);
}
