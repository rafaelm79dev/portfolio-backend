using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ApiUsers.Application.Auth;
using ApiUsers.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ApiUsers.Tests;

[Collection("api")]
public class MeTests(ApiFactory factory)
{
    private HttpClient ClientWithToken(string? token)
    {
        var client = factory.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    private static string CreateToken(
        string key = ApiFactory.SigningKey,
        string audience = ApiFactory.Audience,
        TimeSpan? lifetimeFromNow = null,
        Guid? userId = null)
    {
        var lifetime = lifetimeFromNow ?? TimeSpan.FromMinutes(5);
        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = ApiFactory.Issuer,
            Audience = audience,
            NotBefore = now.AddMinutes(-20),
            IssuedAt = now.AddMinutes(-20),
            Expires = now.Add(lifetime),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = (userId ?? Guid.NewGuid()).ToString(),
                ["email"] = "forged@x.com",
                ["role"] = new[] { "Admin" },
            },
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private static string Base64Url(string json) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Fact]
    public async Task Me_without_token_returns_401()
    {
        var response = await ClientWithToken(null).GetAsync("/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_with_valid_token_returns_user()
    {
        var email = TestHelpers.NewEmail();
        await TestHelpers.RegisterAsync(factory.CreateClient(), email);
        var tokens = await TestHelpers.LoginAsync(factory.CreateClient(), email);

        var response = await ClientWithToken(tokens.AccessToken).GetAsync("/users/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(me);
        Assert.Equal(email, me.Email);
        Assert.Equal(["User"], me.Roles);
    }

    [Fact]
    public async Task Me_with_expired_token_returns_401()
    {
        var token = CreateToken(lifetimeFromNow: TimeSpan.FromMinutes(-10));

        var response = await ClientWithToken(token).GetAsync("/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_with_token_signed_by_other_key_returns_401()
    {
        var token = CreateToken(key: "another-signing-key-another-signing-key-987654");

        var response = await ClientWithToken(token).GetAsync("/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_with_tampered_token_returns_401()
    {
        var email = TestHelpers.NewEmail();
        await TestHelpers.RegisterAsync(factory.CreateClient(), email);
        var tokens = await TestHelpers.LoginAsync(factory.CreateClient(), email);
        var parts = tokens.AccessToken.Split('.');
        var payload = parts[1].ToCharArray();
        payload[10] = payload[10] == 'A' ? 'B' : 'A';
        var tampered = $"{parts[0]}.{new string(payload)}.{parts[2]}";

        var response = await ClientWithToken(tampered).GetAsync("/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_with_unsigned_alg_none_token_returns_401()
    {
        var header = Base64Url("{\"alg\":\"none\",\"typ\":\"JWT\"}");
        var payload = Base64Url(
            $"{{\"sub\":\"{Guid.NewGuid()}\",\"iss\":\"{ApiFactory.Issuer}\",\"aud\":\"{ApiFactory.Audience}\"," +
            $"\"exp\":{DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds()},\"role\":\"Admin\"}}");

        var response = await ClientWithToken($"{header}.{payload}.").GetAsync("/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_with_wrong_audience_returns_401()
    {
        var token = CreateToken(audience: "some-other-audience");

        var response = await ClientWithToken(token).GetAsync("/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_for_user_that_no_longer_exists_returns_401()
    {
        var token = CreateToken(userId: Guid.NewGuid()); // valid signature, but no such user in the database

        var response = await ClientWithToken(token).GetAsync("/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_after_user_is_deleted_returns_401()
    {
        var email = TestHelpers.NewEmail();
        await TestHelpers.RegisterAsync(factory.CreateClient(), email);
        var tokens = await TestHelpers.LoginAsync(factory.CreateClient(), email);
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = await users.FindByEmailAsync(email);
            await users.DeleteAsync(user!);
        }

        var response = await ClientWithToken(tokens.AccessToken).GetAsync("/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
