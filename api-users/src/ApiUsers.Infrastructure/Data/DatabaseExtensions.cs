using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace ApiUsers.Infrastructure.Data;

public static class DatabaseExtensions
{
    /// <summary>
    /// The only place that knows which database provider is used. Switching to PostgreSQL
    /// means changing the provider call here and the connection string.
    /// </summary>
    public static IServiceCollection AddAppDatabase(this IServiceCollection services)
    {
        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("Default")
                ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
            options.UseSqlServer(connectionString);
        });
        return services;
    }

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
}
