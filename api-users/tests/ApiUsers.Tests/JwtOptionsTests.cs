namespace ApiUsers.Tests;

[Collection("api")]
public class JwtOptionsTests(ApiFactory factory)
{
    [Fact]
    public void Startup_fails_when_signing_key_is_too_short()
    {
        var misconfigured = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Jwt:SigningKey", "too-short"));

        Assert.ThrowsAny<Exception>(() => misconfigured.CreateClient());
    }

    [Fact]
    public void Startup_fails_when_issuer_is_missing()
    {
        var misconfigured = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Jwt:Issuer", ""));

        Assert.ThrowsAny<Exception>(() => misconfigured.CreateClient());
    }
}
