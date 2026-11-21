using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;

namespace ApiUsers.Tests;

/// <summary>
/// Starts the API against a throwaway database container. One instance is shared by all test classes
/// through <see cref="ApiCollection"/> so the container is created only once per test run.
/// The engine comes from the TEST_DB_PROVIDER environment variable ("SqlServer" by default, or "PostgreSql").
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminEmail = "admin@test.local";
    public const string AdminPassword = "Adm1n!Passw0rd";
    public const string SigningKey = "test-signing-key-test-signing-key-0123456789";
    public const string Issuer = "api-users-tests";
    public const string Audience = "api-users-tests-clients";

    /// <summary>Database engine under test: "SqlServer" (default) or "PostgreSql", from TEST_DB_PROVIDER.</summary>
    public static string Provider { get; } = Environment.GetEnvironmentVariable("TEST_DB_PROVIDER") ?? "SqlServer";

    private readonly MsSqlContainer? _sqlServer;
    private readonly PostgreSqlContainer? _postgres;

    public ApiFactory()
    {
        switch (Provider)
        {
            case "SqlServer":
                _sqlServer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04").Build();
                break;
            case "PostgreSql":
                _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
                break;
            default:
                throw new InvalidOperationException(
                    $"TEST_DB_PROVIDER must be 'SqlServer' or 'PostgreSql', not '{Provider}'.");
        }
    }

    private string ConnectionString => _sqlServer?.GetConnectionString() ?? _postgres!.GetConnectionString();

    public Task InitializeAsync() => _sqlServer?.StartAsync() ?? _postgres!.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (_sqlServer is not null)
        {
            await _sqlServer.DisposeAsync();
        }
        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:Provider", Provider);
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("Jwt:SigningKey", SigningKey);
        builder.UseSetting("Jwt:Issuer", Issuer);
        builder.UseSetting("Jwt:Audience", Audience);
        builder.UseSetting("Admin:Email", AdminEmail);
        builder.UseSetting("Admin:Password", AdminPassword);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        // High enough that the suite's many /auth calls never hit the limit; rate-limit tests lower it on purpose.
        builder.UseSetting("RateLimiting:AuthPermitLimit", "100000");
    }
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>;
