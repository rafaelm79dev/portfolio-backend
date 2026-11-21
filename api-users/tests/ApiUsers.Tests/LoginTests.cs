using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ApiUsers.Application.Auth;
using ApiUsers.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace ApiUsers.Tests;

[Collection("api")]
public class LoginTests(ApiFactory factory)
{
    private Task<HttpResponseMessage> LoginRawAsync(string? email, string? password) =>
        factory.CreateClient().PostAsJsonAsync("/auth/login", new LoginRequest(email, password));

    private static async Task<string?> DetailAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.TryGetProperty("detail", out var detail) ? detail.GetString() : null;
    }

    [Fact]
    public async Task Login_valid_returns_tokens()
    {
        var email = await TestHelpers.RegisterNewUserAsync(factory.CreateClient());

        var tokens = await TestHelpers.LoginAsync(factory.CreateClient(), email);

        Assert.False(string.IsNullOrWhiteSpace(tokens.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(tokens.RefreshToken));
        var accessLifetime = tokens.AccessTokenExpiresAtUtc - DateTime.UtcNow;
        Assert.InRange(accessLifetime, TimeSpan.FromMinutes(ApiFactory.AccessTokenMinutes - 1), TimeSpan.FromMinutes(ApiFactory.AccessTokenMinutes));
        var refreshLifetime = tokens.RefreshTokenExpiresAtUtc - DateTime.UtcNow;
        Assert.InRange(refreshLifetime, TimeSpan.FromDays(ApiFactory.RefreshTokenDays - 0.01), TimeSpan.FromDays(ApiFactory.RefreshTokenDays));
    }

    [Fact]
    public async Task Access_token_has_expected_claims()
    {
        var email = await TestHelpers.RegisterNewUserAsync(factory.CreateClient());
        var tokens = await TestHelpers.LoginAsync(factory.CreateClient(), email);

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(tokens.AccessToken);

        Assert.True(Guid.TryParse(jwt.Subject, out _));
        Assert.Equal(email, jwt.GetClaim("email").Value);
        Assert.Equal("User", jwt.GetClaim("role").Value);
        Assert.Equal(ApiFactory.Issuer, jwt.Issuer);
        Assert.Contains(ApiFactory.Audience, jwt.Audiences);
    }

    [Fact]
    public async Task Login_wrong_password_returns_401()
    {
        var email = await TestHelpers.RegisterNewUserAsync(factory.CreateClient());

        var response = await LoginRawAsync(email, "Wr0ng!Password");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_unknown_email_returns_401_with_same_detail_as_wrong_password()
    {
        var email = await TestHelpers.RegisterNewUserAsync(factory.CreateClient());

        var wrongPassword = await LoginRawAsync(email, "Wr0ng!Password");
        var unknownEmail = await LoginRawAsync(TestHelpers.NewEmail(), "Wr0ng!Password");

        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        Assert.NotNull(await DetailAsync(wrongPassword));
        Assert.Equal(await DetailAsync(wrongPassword), await DetailAsync(unknownEmail));
    }

    [Fact]
    public async Task Login_email_is_case_insensitive()
    {
        var local = Guid.NewGuid().ToString("N");
        await TestHelpers.RegisterAsync(factory.CreateClient(), $"{local}@x.com");

        var response = await LoginRawAsync($"{local.ToUpperInvariant()}@X.COM", TestHelpers.Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_locks_out_after_5_failures()
    {
        var email = await TestHelpers.RegisterNewUserAsync(factory.CreateClient());
        await TestHelpers.LockOutAsync(factory.CreateClient(), email);

        var response = await LoginRawAsync(email, TestHelpers.Password);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("{\"email\":\"a@x.com\"}")]
    public async Task Login_empty_or_incomplete_body_returns_400(string body)
    {
        var content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await factory.CreateClient().PostAsync("/auth/login", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_token_is_stored_hashed()
    {
        var email = await TestHelpers.RegisterNewUserAsync(factory.CreateClient());
        var tokens = await TestHelpers.LoginAsync(factory.CreateClient(), email);
        var expectedHash = TestHelpers.HashToken(tokens.RefreshToken);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hashes = await db.RefreshTokens.AsNoTracking().Select(t => t.TokenHash).ToListAsync();

        Assert.DoesNotContain(tokens.RefreshToken, hashes);
        Assert.Contains(expectedHash, hashes);
    }

    [Fact]
    public async Task Refresh_token_is_32_random_bytes_encoded_as_43_base64url_characters()
    {
        var email = await TestHelpers.RegisterNewUserAsync(factory.CreateClient());

        var first = await TestHelpers.LoginAsync(factory.CreateClient(), email);
        var second = await TestHelpers.LoginAsync(factory.CreateClient(), email);

        Assert.Matches("^[A-Za-z0-9_-]{43}$", first.RefreshToken); // 32 bytes -> 43 characters, no padding
        Assert.Matches("^[A-Za-z0-9_-]{43}$", second.RefreshToken);
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
    }
}
