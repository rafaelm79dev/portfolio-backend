using System.Net;
using System.Net.Http.Json;
using ApiUsers.Application.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace ApiUsers.Tests;

[Collection("api")]
public class ForwardedHeadersTests(ApiFactory factory)
{
    private const string ProxyIp = "10.0.0.1";

    /// <summary>The test server has no real socket, so this stands in for the address of the connecting peer.</summary>
    private sealed class FixedRemoteIpFilter(IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = address;
                return nextMiddleware(context);
            });
            next(app);
        };
    }

    private WebApplicationFactory<Program> HostBehind(string peerIp, Action<IWebHostBuilder> configure) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IStartupFilter>(new FixedRemoteIpFilter(IPAddress.Parse(peerIp))));
            configure(builder);
        });

    private static void TrustProxyAndLimitTo(IWebHostBuilder builder, int permits) => builder
        .UseSetting("ForwardedHeaders:Enabled", "true")
        .UseSetting("ForwardedHeaders:KnownProxies:0", ProxyIp)
        .UseSetting("RateLimiting:AuthPermitLimit", permits.ToString());

    private static async Task<HttpStatusCode> LoginAsync(HttpClient client, string? forwardedFor)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(TestHelpers.NewEmail(), "Wr0ng!Password")),
        };
        if (forwardedFor is not null)
        {
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        }
        return (await client.SendAsync(request)).StatusCode;
    }

    [Fact]
    public async Task Each_forwarded_client_ip_gets_its_own_rate_limit_quota_behind_a_trusted_proxy()
    {
        var client = HostBehind(ProxyIp, b => TrustProxyAndLimitTo(b, permits: 2)).CreateClient();

        var firstClient = new List<HttpStatusCode>
        {
            await LoginAsync(client, "203.0.113.1"),
            await LoginAsync(client, "203.0.113.1"),
            await LoginAsync(client, "203.0.113.1"),
        };
        var secondClient = await LoginAsync(client, "203.0.113.2");

        Assert.Equal(HttpStatusCode.TooManyRequests, firstClient[2]);
        Assert.Equal(HttpStatusCode.Unauthorized, secondClient);
    }

    [Fact]
    public async Task Forwarded_headers_are_ignored_by_default()
    {
        var client = HostBehind(ProxyIp, b => b.UseSetting("RateLimiting:AuthPermitLimit", "2")).CreateClient();

        var statuses = new[]
        {
            await LoginAsync(client, "203.0.113.1"),
            await LoginAsync(client, "203.0.113.2"),
            await LoginAsync(client, "203.0.113.3"), // a different "client" each time, but the same peer
        };

        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[2]);
    }

    [Fact]
    public async Task A_peer_that_is_not_a_known_proxy_cannot_spoof_the_client_ip()
    {
        var client = HostBehind("10.0.0.99", b => TrustProxyAndLimitTo(b, permits: 2)).CreateClient();

        var statuses = new[]
        {
            await LoginAsync(client, "203.0.113.1"),
            await LoginAsync(client, "203.0.113.2"),
            await LoginAsync(client, "203.0.113.3"),
        };

        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[2]);
    }

    [Fact]
    public async Task Https_redirect_does_not_loop_when_the_trusted_proxy_forwards_https()
    {
        var host = HostBehind(ProxyIp, b =>
        {
            TrustProxyAndLimitTo(b, permits: 100);
            b.UseEnvironment("Production").UseSetting("HTTPS_PORT", "443");
        });
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var forwarded = new HttpRequestMessage(HttpMethod.Get, "/health");
        forwarded.Headers.Add("X-Forwarded-Proto", "https");
        var behindProxy = await client.SendAsync(forwarded);
        var direct = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, behindProxy.StatusCode);
        Assert.True(direct.StatusCode is HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect);
    }

    [Fact]
    public void Startup_fails_when_forwarding_is_enabled_without_any_known_proxy()
    {
        // With empty lists the framework would trust every client: refuse to start instead.
        var misconfigured = factory.WithWebHostBuilder(b => b.UseSetting("ForwardedHeaders:Enabled", "true"));

        Assert.ThrowsAny<Exception>(() => misconfigured.CreateClient());
    }

    [Theory]
    [InlineData("ForwardedHeaders:KnownProxies:0", "not-an-ip")]
    [InlineData("ForwardedHeaders:KnownNetworks:0", "not-a-cidr")]
    public void Startup_fails_when_a_proxy_or_network_is_not_valid(string key, string value)
    {
        var misconfigured = factory.WithWebHostBuilder(b => b
            .UseSetting("ForwardedHeaders:Enabled", "true")
            .UseSetting(key, value));

        Assert.ThrowsAny<Exception>(() => misconfigured.CreateClient());
    }
}
