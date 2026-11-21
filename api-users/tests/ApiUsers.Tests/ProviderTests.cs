using ApiUsers.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace ApiUsers.Tests;

[Collection("api")]
public class ProviderTests(ApiFactory factory)
{
    [Fact]
    public void Startup_fails_with_unknown_provider()
    {
        using var misconfigured = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Database:Provider", "Oracle"));

        StartupAssert.FailsOptionsValidation(() => misconfigured.CreateClient(), "Database:Provider");
    }

    [Fact]
    public void Startup_assertion_rejects_a_failure_that_is_not_options_validation()
    {
        // An empty connection string also stops the application, but not through options validation.
        using var unrelated = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:Default", ""));

        var rejected = Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
            StartupAssert.FailsOptionsValidation(() => unrelated.CreateClient(), "Jwt:Issuer"));

        Assert.Contains("Expected OptionsValidationException", rejected.Message);
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
