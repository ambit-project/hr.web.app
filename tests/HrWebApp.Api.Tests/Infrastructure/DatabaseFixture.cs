using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Networks;
using JetBrains.Annotations;
using Npgsql;
using Testcontainers.PostgreSql;

namespace HrWebApp.Api.Tests.Infrastructure;

/// <summary>
/// PostgreSQL 18 w kontenerze, role z db/bootstrap/roles.sql i migracje Liquibase z db/ — tak samo jak w compose.
/// </summary>
[UsedImplicitly]
public sealed class DatabaseFixture : IAsyncLifetime
{
    private const string PostgresImage = "postgres:18.6";
    private const string LiquibaseImage = "liquibase/liquibase:4.33.0";
    private const string NetworkAlias = "pg";
    private const string ApiPassword = "test-api-password";
    private const string MigratorPassword = "test-migrator-password";

    private readonly INetwork _network = new NetworkBuilder().Build();
    private PostgreSqlContainer? _postgres;
    private ApiFactory? _api;

    /// <summary>Jedna aplikacja (WebApplicationFactory) na całą kolekcję testów z bazą.</summary>
    public ApiFactory Api => _api ??= new ApiFactory(this);

    /// <summary>Połączenie jako rola API (tylko EXECUTE na rutynach &lt;moduł&gt;_app).</summary>
    public string ApiConnectionString { get; private set; } = "";

    /// <summary>Połączenie jako superużytkownik — tylko do aranżacji i sprawdzeń obok kontraktu.</summary>
    public string AdminConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        await _network.CreateAsync();
        _postgres = new PostgreSqlBuilder(PostgresImage)
            .WithNetwork(_network)
            .WithNetworkAliases(NetworkAlias)
            .Build();
        await _postgres.StartAsync();

        await CreateRolesAsync(_postgres);
        await MigrateAsync();

        var admin = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Database = "hr",
        };
        AdminConnectionString = admin.ConnectionString;
        ApiConnectionString = new NpgsqlConnectionStringBuilder(admin.ConnectionString)
        {
            Username = "hr_api",
            Password = ApiPassword,
        }.ConnectionString;
    }

    public async Task DisposeAsync()
    {
        if (_api is not null)
        {
            await _api.DisposeAsync();
        }

        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }

        await _network.DisposeAsync();
    }

    private static async Task CreateRolesAsync(PostgreSqlContainer postgres)
    {
        var script = await File.ReadAllBytesAsync(
            RepoPaths.Combine("db", "bootstrap", "roles.sql")
        );
        await postgres.CopyAsync(script, "/tmp/roles.sql");
        var result = await postgres.ExecAsync([
            "psql",
            "-U",
            "postgres",
            "-v",
            "db=hr",
            "-v",
            "prefix=hr",
            "-v",
            $"migrator_password={MigratorPassword}",
            "-v",
            $"api_password={ApiPassword}",
            "-f",
            "/tmp/roles.sql",
        ]);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"roles.sql: {result.Stderr}");
        }
    }

    private async Task MigrateAsync()
    {
        await using var liquibase = new ContainerBuilder(LiquibaseImage)
            .WithNetwork(_network)
            .WithBindMount(RepoPaths.Combine("db"), "/liquibase/changelog", AccessMode.ReadOnly)
            .WithCommand(
                $"--url=jdbc:postgresql://{NetworkAlias}:5432/hr",
                "--username=hr_migrator",
                $"--password={MigratorPassword}",
                "--search-path=/liquibase/changelog",
                "--changelog-file=changelog.yaml",
                "update",
                "--contexts=dev",
                "-Dapi_role=hr_api"
            )
            .Build();
        await liquibase.StartAsync();

        // GetExitCodeAsync czeka na zakończenie kontenera (docker wait).
        var exitCode = await liquibase.GetExitCodeAsync();
        if (exitCode != 0)
        {
            var (stdout, stderr) = await liquibase.GetLogsAsync();
            throw new InvalidOperationException(
                new StringBuilder("Liquibase update failed:\n")
                    .Append(stdout)
                    .Append(stderr)
                    .ToString()
            );
        }
    }
}

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "database";
}
