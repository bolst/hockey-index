using System.Threading.RateLimiting;
using HockeyIndex.Api.Infrastructure.Auth;
using HockeyIndex.Api.Infrastructure.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace HockeyIndex.Api.Infrastructure.RateLimiting;

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    public int PublicReadPerMinute { get; set; } = 120;
    public int PublicReadEdgePerMinute { get; set; } = 600;
    public int VenueLookupPerMinute { get; set; } = 30;
    public int HostWritePerMinute { get; set; } = 60;
    public int AdminPerMinute { get; set; } = 120;
}

public static class RateLimitPolicies
{
    public const string PublicRead = "public-read";
    public const string PublicReadEdgePartition = "public-read-edge";
    public const string VenueLookup = "venue-lookup";
    public const string HostWrite = "host-write";
    public const string Admin = "admin";

    public static IServiceCollection AddHockeyIndexRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RateLimitOptions>(configuration.GetSection(RateLimitOptions.SectionName));
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = WriteRejectionAsync;
            limiter.AddPolicy(PublicRead, PublicReadPartition);
            limiter.AddPolicy(VenueLookup, context => PerHostPartition(context, VenueLookup, limits => limits.VenueLookupPerMinute));
            limiter.AddPolicy(HostWrite, context => PerHostPartition(context, HostWrite, limits => limits.HostWritePerMinute));
            limiter.AddPolicy(Admin, context => PerHostPartition(context, Admin, limits => limits.AdminPerMinute));
        });

        return services;
    }

    internal static RateLimitPartition<string> PublicReadPartition(HttpContext context)
    {
        if (context.IsOpsListener())
        {
            return RateLimitPartition.GetNoLimiter("ops");
        }

        var limits = context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;

        // A valid edge key moves the request to one shared bucket for the Pages Function; it is not an exemption.
        return context.IsFromEdge()
            ? RateLimitPartition.GetTokenBucketLimiter(PublicReadEdgePartition, _ => PerMinute(limits.PublicReadEdgePerMinute))
            : RateLimitPartition.GetTokenBucketLimiter($"{PublicRead}:{context.GetClientIp().KeyHex}", _ => PerMinute(limits.PublicReadPerMinute));
    }

    /// <summary>Keyed by the signed-in host; requires the rate limiter to run after authentication.</summary>
    internal static RateLimitPartition<string> PerHostPartition(HttpContext context, string policy, Func<RateLimitOptions, int> permitsPerMinute)
    {
        var limits = context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;
        var key = context.User.GetHostId() is { } hostId ? hostId.ToString("N") : context.GetClientIp().KeyHex;
        return RateLimitPartition.GetTokenBucketLimiter($"{policy}:{key}", _ => PerMinute(permitsPerMinute(limits)));
    }

    private static TokenBucketRateLimiterOptions PerMinute(int permits) => new()
    {
        TokenLimit = permits,
        TokensPerPeriod = permits,
        ReplenishmentPeriod = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
        AutoReplenishment = true,
    };

    private static async ValueTask WriteRejectionAsync(OnRejectedContext rejected, CancellationToken cancellationToken)
    {
        var problemDetails = rejected.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = rejected.HttpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too many requests",
            },
        });
    }
}
