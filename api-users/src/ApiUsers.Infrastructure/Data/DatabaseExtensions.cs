using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ApiUsers.Infrastructure.Data;

public static class DatabaseExtensions
{
    // Each provider keeps its own migrations (the SQL differs), in a separate assembly selected by name.
    private const string SqlServerMigrationsAssembly = "ApiUsers.Migrations.SqlServer";
    private const string PostgreSqlMigrationsAssembly = "ApiUsers.Migrations.PostgreSql";

    /// <summary>
    /// The only place that knows which database providers exist. The engine is chosen with
    /// <c>Database:Provider</c>; an unknown value stops the application at startup.
    /// </summary>
    public static IServiceCollection AddAppDatabase(this IServiceCollection services)
    {
        services.AddOptions<DatabaseOptions>()
            .BindConfiguration(DatabaseOptions.SectionName)
            .Validate(o => IsKnownProvider(o.Provider),
                $"Database:Provider must be '{DatabaseOptions.SqlServer}' or '{DatabaseOptions.PostgreSql}'.")
            .ValidateOnStart();

        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("Default")
                ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
            var provider = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.Provider;

            if (string.Equals(provider, DatabaseOptions.PostgreSql, StringComparison.OrdinalIgnoreCase))
            {
                options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(PostgreSqlMigrationsAssembly));
            }
            else
            {
                options.UseSqlServer(connectionString, sql => sql.MigrationsAssembly(SqlServerMigrationsAssembly));
            }
        });
        return services;
    }

    private static bool IsKnownProvider(string? provider) =>
        string.Equals(provider, DatabaseOptions.SqlServer, StringComparison.OrdinalIgnoreCase)
        || string.Equals(provider, DatabaseOptions.PostgreSql, StringComparison.OrdinalIgnoreCase);

    /// <summary>Applies pending migrations when <c>Database:MigrateOnStartup</c> is true.</summary>
    public static async Task MigrateDatabaseAsync(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
        {
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }

    /// <summary>
    /// Creates the roles and the initial admin. Runs under the same <c>Database:MigrateOnStartup</c> switch
    /// as the migrations, because it needs the schema to exist.
    /// </summary>
    public static async Task SeedDatabaseAsync(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
        {
            return;
        }

        await DbSeeder.SeedAsync(app.Services, app.Configuration);
    }
}
