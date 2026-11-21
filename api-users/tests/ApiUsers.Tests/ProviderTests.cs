using ApiUsers.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace ApiUsers.Tests;

[Collection("api")]
public class ProviderTests(ApiFactory factory)
{
    [Fact]
    public void Startup_fails_with_unknown_provider()
    {
        var misconfigured = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Database:Provider", "Oracle"));

        Assert.ThrowsAny<Exception>(() => misconfigured.CreateClient());
    }

    [Fact]
    public void Active_provider_matches_test_configuration()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var expected = ApiFactory.Provider == "PostgreSql"
            ? "Npgsql.EntityFrameworkCore.PostgreSQL"
            : "Microsoft.EntityFrameworkCore.SqlServer";
        Assert.Equal(expected, db.Database.ProviderName);
    }
}
