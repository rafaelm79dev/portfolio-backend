using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using IPNetwork = System.Net.IPNetwork; // the older Microsoft.AspNetCore.HttpOverrides.IPNetwork is obsolete

namespace ApiUsers.Api.Security;

public sealed class ForwardedHeadersSettings
{
    public const string SectionName = "ForwardedHeaders";

    /// <summary>Trust X-Forwarded-For / X-Forwarded-Proto sent by the proxies listed below. Off by default.</summary>
    public bool Enabled { get; set; }

    /// <summary>Addresses of the reverse proxies allowed to set the forwarded headers.</summary>
    public string[] KnownProxies { get; set; } = [];

    /// <summary>Networks (CIDR, for example 10.0.0.0/8) whose hosts are trusted reverse proxies.</summary>
    public string[] KnownNetworks { get; set; } = [];

    public int ForwardLimit { get; set; } = 1;
}

public static class ForwardedHeadersExtensions
{
    /// <summary>
    /// Opt-in support for running behind a reverse proxy that terminates TLS. It is off by default because
    /// honouring forwarded headers from an arbitrary client would let anyone fake their IP and dodge the rate limit.
    /// When enabled, the trusted proxies must be listed explicitly: with empty lists the framework trusts everyone.
    /// </summary>
    public static IServiceCollection AddAppForwardedHeaders(this IServiceCollection services)
    {
        services.AddOptions<ForwardedHeadersSettings>()
            .BindConfiguration(ForwardedHeadersSettings.SectionName)
            .Validate(s => !s.Enabled || s.KnownProxies.Length + s.KnownNetworks.Length > 0,
                "ForwardedHeaders:Enabled requires at least one entry in KnownProxies or KnownNetworks.")
            .Validate(s => s.KnownProxies.All(p => IPAddress.TryParse(p, out _)),
                "ForwardedHeaders:KnownProxies must contain valid IP addresses.")
            .Validate(s => s.KnownNetworks.All(n => IPNetwork.TryParse(n, out _)),
                "ForwardedHeaders:KnownNetworks must contain valid CIDR networks.")
            .Validate(s => s.ForwardLimit > 0, "ForwardedHeaders:ForwardLimit must be positive.")
            .ValidateOnStart();

        // Configured through options so the values are read once the configuration is fully built.
        services.AddOptions<ForwardedHeadersOptions>()
            .Configure<IOptions<ForwardedHeadersSettings>>((options, settings) =>
            {
                var config = settings.Value;
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.ForwardLimit = config.ForwardLimit;

                // Drop the loopback defaults: only the configured proxies are trusted.
                options.KnownProxies.Clear();
                options.KnownIPNetworks.Clear();
                foreach (var proxy in config.KnownProxies)
                {
                    options.KnownProxies.Add(IPAddress.Parse(proxy));
                }
                foreach (var network in config.KnownNetworks)
                {
                    options.KnownIPNetworks.Add(IPNetwork.Parse(network));
                }
            });

        return services;
    }

    public static WebApplication UseAppForwardedHeaders(this WebApplication app)
    {
        if (app.Services.GetRequiredService<IOptions<ForwardedHeadersSettings>>().Value.Enabled)
        {
            app.UseForwardedHeaders();
        }
        return app;
    }
}
