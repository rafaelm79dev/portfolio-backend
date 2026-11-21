using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace ApiUsers.Api.Security;

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Requests allowed to <c>/auth/*</c> per client IP in each window.</summary>
    public int AuthPermitLimit { get; set; } = 20;

    public int AuthWindowSeconds { get; set; } = 60;
}

public static class RateLimitingExtensions
{
    public const string AuthPolicy = "auth";

    /// <summary>
    /// Fixed-window limit per client IP for the authentication endpoints. Behind a reverse proxy, forward the real
    /// client address (ForwardedHeaders) or every client will share the proxy's IP and its quota.
    /// </summary>
    public static IServiceCollection AddAppRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<RateLimitOptions>()
            .BindConfiguration(RateLimitOptions.SectionName)
            .Validate(o => o.AuthPermitLimit > 0 && o.AuthWindowSeconds > 0,
                "RateLimiting values must be positive.")
            .ValidateOnStart();

        services.AddRateLimiter();

        // Configured through options so the values are read once the configuration is fully built.
        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<RateLimitOptions>>((limiter, config) =>
            {
                var settings = config.Value;
                limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                limiter.AddPolicy(AuthPolicy, httpContext => RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = settings.AuthPermitLimit,
                        Window = TimeSpan.FromSeconds(settings.AuthWindowSeconds),
                        QueueLimit = 0,
                    }));
                limiter.OnRejected = async (context, cancellationToken) =>
                {
                    if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    {
                        context.HttpContext.Response.Headers.RetryAfter =
                            ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
                    }

                    await Results.Problem(
                            statusCode: StatusCodes.Status429TooManyRequests,
                            title: "Too Many Requests",
                            detail: "Too many requests. Try again later.")
                        .ExecuteAsync(context.HttpContext);
                };
            });

        return services;
    }
}
