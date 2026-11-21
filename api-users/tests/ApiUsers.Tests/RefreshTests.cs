using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using ApiUsers.Application.Auth;
using ApiUsers.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApiUsers.Tests;

[Collection("api")]
public class RefreshTests(ApiFactory factory)
{
    private static string Hash(string token) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private Task<HttpResponseMessage> RefreshAsync(string? refreshToken) =>
        factory.CreateClient().PostAsJsonAsync("/auth/refresh", new RefreshRequest(refreshToken));

    private Task<HttpResponseMessage> LogoutAsync(string? accessToken, string? refreshToken)
    {
        var client = factory.CreateClient();
        if (accessToken is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }
        return client.PostAsJsonAsync("/auth/logout", new RefreshRequest(refreshToken));
    }

    private async Task<TokenResponse> NewSessionAsync()
    {
        var email = TestHelpers.NewEmail();
        await TestHelpers.RegisterAsync(factory.CreateClient(), email);
        return await TestHelpers.LoginAsync(factory.CreateClient(), email);
    }

    [Fact]
    public async Task Refresh_returns_new_token_pair()
    {
        var first = await NewSessionAsync();

        var response = await RefreshAsync(first.RefreshToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var second = await response.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(second);
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", second.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/users/me")).StatusCode);
    }

    [Fact]
    public async Task Refresh_reusing_rotated_token_returns_401_and_revokes_family()
    {
        var first = await NewSessionAsync();
        var second = await (await RefreshAsync(first.RefreshToken)).Content.ReadFromJsonAsync<TokenResponse>();

        var reuse = await RefreshAsync(first.RefreshToken);
        var newestAfterReuse = await RefreshAsync(second!.RefreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, newestAfterReuse.StatusCode);
    }

    [Fact]
    public async Task Refresh_with_expired_token_returns_401()
    {
        var session = await NewSessionAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hash = Hash(session.RefreshToken);
            await db.RefreshTokens.Where(t => t.TokenHash == hash)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ExpiresAtUtc, DateTime.UtcNow.AddMinutes(-1)));
        }

        var response = await RefreshAsync(session.RefreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("not-a-real-token-but-looks-like-one-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task Refresh_with_unknown_token_returns_401(string token)
    {
        var response = await RefreshAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_with_huge_token_returns_401()
    {
        var response = await RefreshAsync(new string('A', 5000));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task Refresh_with_missing_token_returns_400(string? token)
    {
        var response = await RefreshAsync(token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_concurrent_requests_only_one_succeeds()
    {
        var session = await NewSessionAsync();

        var responses = await Task.WhenAll(
            RefreshAsync(session.RefreshToken),
            RefreshAsync(session.RefreshToken));

        var statuses = responses.Select(r => r.StatusCode).OrderBy(s => (int)s).ToArray();
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Unauthorized], statuses);
    }

    [Fact]
    public async Task Logout_revokes_refresh_token()
    {
        var session = await NewSessionAsync();

        var logout = await LogoutAsync(session.AccessToken, session.RefreshToken);
        var refresh = await RefreshAsync(session.RefreshToken);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Logout_without_auth_returns_401()
    {
        var session = await NewSessionAsync();

        var response = await LogoutAsync(null, session.RefreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_is_idempotent()
    {
        var session = await NewSessionAsync();

        var first = await LogoutAsync(session.AccessToken, session.RefreshToken);
        var second = await LogoutAsync(session.AccessToken, session.RefreshToken);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
    }

    [Fact]
    public async Task Logout_with_other_users_token_does_not_revoke_it()
    {
        var owner = await NewSessionAsync();
        var intruder = await NewSessionAsync();

        var logout = await LogoutAsync(intruder.AccessToken, owner.RefreshToken);
        var refresh = await RefreshAsync(owner.RefreshToken);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
    }

    [Fact]
    public async Task Logout_with_missing_refresh_token_returns_400()
    {
        var session = await NewSessionAsync();

        var response = await LogoutAsync(session.AccessToken, null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
