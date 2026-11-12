using Npgsql;

namespace HrWebApp.Api.Database;

public static class DatabaseSetup
{
    private const string ConnectionStringName = "Hr";

    public static IServiceCollection AddDatabase(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Brak connection stringa '{ConnectionStringName}'. Ustaw zmienną ConnectionStrings__{ConnectionStringName} (rola <prefix>_api)."
            );
        }

        services.AddSingleton(_ => new NpgsqlDataSourceBuilder(connectionString).Build());
        services.AddScoped<DbSession>();
        return services;
    }
}
