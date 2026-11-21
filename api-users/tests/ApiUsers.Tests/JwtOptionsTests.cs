namespace ApiUsers.Tests;

[Collection("api")]
public class JwtOptionsTests(ApiFactory factory)
{
    [Fact]
    public void Startup_fails_when_signing_key_is_too_short()
    {
        using var misconfigured = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Jwt:SigningKey", "too-short"));

        StartupAssert.FailsOptionsValidation(() => misconfigured.CreateClient(), "Jwt:SigningKey");
    }

    [Fact]
    public void Startup_fails_when_issuer_is_missing()
    {
        using var misconfigured = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Jwt:Issuer", ""));

        StartupAssert.FailsOptionsValidation(() => misconfigured.CreateClient(), "Jwt:Issuer");
    }
}
