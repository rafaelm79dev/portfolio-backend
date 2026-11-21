using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ApiUsers.Application.Auth;
using ApiUsers.Domain;
using ApiUsers.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ApiUsers.Tests;

[Collection("api")]
public class HardeningTests(ApiFactory factory)
{
    private const string WrongPassword = TestHelpers.WrongPassword;

    private static Task<HttpResponseMessage> Post(HttpClient client, string path, object body) =>
        client.PostAsJsonAsync(path, body);

    private static Task<HttpResponseMessage> TryLogin(HttpClient client, string email, string password) =>
        Post(client, "/auth/login", new LoginRequest(email, password));

    // ---- F: simultaneous registrations of the same email -------------------------------------------------

    [Fact]
    public async Task Register_same_email_concurrently_gives_one_created_and_never_a_server_error()
    {
        // Several rounds, because the race window is small.
        for (var round = 0; round < 8; round++)
        {
            var email = TestHelpers.NewEmail();

            var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
                Post(factory.CreateClient(), "/auth/register", new RegisterRequest(email, TestHelpers.Password))));

            var statuses = responses.Select(r => r.StatusCode).ToList();
            Assert.Equal(1, statuses.Count(s => s == HttpStatusCode.Created));
            Assert.Equal(7, statuses.Count(s => s == HttpStatusCode.Conflict));
        }
    }

    // ---- A: rate limiting on /auth ----------------------------------------------------------------------

    [Fact]
    public async Task Auth_endpoints_return_429_with_retry_after_once_the_limit_is_exceeded()
    {
        using var limited = factory.WithWebHostBuilder(builder => builder
            .UseSetting("RateLimiting:AuthPermitLimit", "3")
            .UseSetting("RateLimiting:AuthWindowSeconds", "60"));
        var client = limited.CreateClient();

        var responses = new List<HttpResponseMessage>();
        for (var i = 0; i < 5; i++)
        {
            responses.Add(await TryLogin(client, TestHelpers.NewEmail(), WrongPassword));
        }

        Assert.All(responses.Take(3), r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));
        Assert.Equal(HttpStatusCode.TooManyRequests, responses[3].StatusCode);
        Assert.True(responses[3].Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task Health_is_not_rate_limited()
    {
        using var limited = factory.WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:AuthPermitLimit", "1"));
        var client = limited.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        }
    }

    // ---- B: sessions and roles are checked against the database -----------------------------------------

    [Fact]
    public async Task Refresh_with_locked_out_account_returns_401()
    {
        var email = await TestHelpers.RegisterNewUserAsync(factory.CreateClient());
        var tokens = await TestHelpers.LoginAsync(factory.CreateClient(), email);
        await TestHelpers.LockOutAsync(factory.CreateClient(), email);

        var response = await Post(factory.CreateClient(), "/auth/refresh", new RefreshRequest(tokens.RefreshToken));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Users_list_after_admin_role_is_removed_returns_403_even_with_a_valid_token()
    {
        var email = await TestHelpers.RegisterNewUserAsync(factory.CreateClient());
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            await users.AddToRoleAsync((await users.FindByEmailAsync(email))!, Roles.Admin);
        }
        var tokens = await TestHelpers.LoginAsync(factory.CreateClient(), email);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/users")).StatusCode); // token carries role Admin

        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            await users.RemoveFromRoleAsync((await users.FindByEmailAsync(email))!, Roles.Admin);
        }
        var response = await client.GetAsync("/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- C: password length ------------------------------------------------------------------------------

    [Fact]
    public async Task Register_password_shorter_than_10_characters_returns_400()
    {
        var response = await Post(factory.CreateClient(), "/auth/register",
            new RegisterRequest(TestHelpers.NewEmail(), "Aa1!aaaa")); // 8 characters, otherwise valid

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_password_of_exactly_10_characters_is_accepted()
    {
        var response = await Post(factory.CreateClient(), "/auth/register",
            new RegisterRequest(TestHelpers.NewEmail(), "Aa1!aaaaaa"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // ---- D: HTTPS outside Development --------------------------------------------------------------------

    [Fact]
    public async Task Production_redirects_http_to_https()
    {
        using var production = factory.WithWebHostBuilder(builder => builder
            .UseEnvironment("Production")
            .UseSetting("HTTPS_PORT", "443"));
        var client = production.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/health");

        Assert.True(response.StatusCode is HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect);
        Assert.StartsWith("https://", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Production_sends_hsts_over_https()
    {
        using var production = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        var client = production.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://example.com"),
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task Development_does_not_redirect_to_https()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- E: a locked account costs the same hashing work as an unknown email -----------------------------

    private sealed class CountingHasher : IPasswordHasher<AppUser>
    {
        private readonly PasswordHasher<AppUser> _inner = new();
        public int HashCalls;
        public int VerifyCalls;

        public string HashPassword(AppUser user, string password)
        {
            Interlocked.Increment(ref HashCalls);
            return _inner.HashPassword(user, password);
        }

        public PasswordVerificationResult VerifyHashedPassword(AppUser user, string hashedPassword, string providedPassword)
        {
            Interlocked.Increment(ref VerifyCalls);
            return _inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
        }

        public void Reset() => HashCalls = VerifyCalls = 0;
    }

    private sealed record CountingHost(WebApplicationFactory<Program> Host, HttpClient Client, CountingHasher Hasher) : IDisposable
    {
        public void Dispose() => Host.Dispose();
    }

    private CountingHost ClientWithCountingHasher()
    {
        var hasher = new CountingHasher();
        var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPasswordHasher<AppUser>>();
            services.AddSingleton<IPasswordHasher<AppUser>>(hasher);
        }));
        return new CountingHost(host, host.CreateClient(), hasher);
    }

    [Fact]
    public async Task Unknown_email_pays_one_decoy_hash()
    {
        using var counting = ClientWithCountingHasher();
        var (client, hasher) = (counting.Client, counting.Hasher);
        hasher.Reset();

        await TryLogin(client, TestHelpers.NewEmail(), WrongPassword);

        Assert.Equal(1, hasher.HashCalls);
    }

    [Fact]
    public async Task Locked_account_pays_one_decoy_hash_too()
    {
        using var counting = ClientWithCountingHasher();
        var (client, hasher) = (counting.Client, counting.Hasher);
        var email = TestHelpers.NewEmail();
        await TestHelpers.RegisterAsync(client, email);
        await TestHelpers.LockOutAsync(client, email);
        hasher.Reset();

        var response = await TryLogin(client, email, TestHelpers.Password);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, hasher.HashCalls);
    }

    [Fact]
    public async Task Successful_login_verifies_the_password_without_a_decoy()
    {
        using var counting = ClientWithCountingHasher();
        var (client, hasher) = (counting.Client, counting.Hasher);
        var email = TestHelpers.NewEmail();
        await TestHelpers.RegisterAsync(client, email);
        hasher.Reset();

        await TestHelpers.LoginAsync(client, email);

        Assert.Equal(0, hasher.HashCalls);
        Assert.Equal(1, hasher.VerifyCalls);
    }
}
