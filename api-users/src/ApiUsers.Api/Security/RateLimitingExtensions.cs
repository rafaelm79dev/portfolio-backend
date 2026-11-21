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
    private const int SegmentsPerWindow = 6;

    /// <summary>
    /// Sliding-window limit per client IP for the authentication endpoints. Counters live in memory, so each
    /// instance keeps its own. Behind a reverse proxy, enable ForwardedHeaders (see ForwardedHeadersExtensions) or
    /// every client will share the proxy's IP and its quota.
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
                // Sliding window: unlike a fixed window it cannot let two full bursts through around a window edge.
                limiter.AddPolicy(AuthPolicy, httpContext => RateLimitPartition.GetSlidingWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = settings.AuthPermitLimit,
                        Window = TimeSpan.FromSeconds(settings.AuthWindowSeconds),
                        SegmentsPerWindow = SegmentsPerWindow,
                        QueueLimit = 0,
                    }));
                limiter.OnRejected = async (context, cancellationToken) =>
                {
                    // The sliding window does not report when to retry. A permit frees up as soon as the oldest
                    // segment leaves the window, so one segment is the longest a client has to wait.
                    var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var reported)
                        ? reported
                        : TimeSpan.FromSeconds((double)settings.AuthWindowSeconds / SegmentsPerWindow);
                    context.HttpContext.Response.Headers.RetryAfter =
                        Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();

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
