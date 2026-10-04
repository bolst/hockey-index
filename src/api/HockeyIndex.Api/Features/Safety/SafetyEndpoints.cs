using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Features.Discovery;
using HockeyIndex.Api.Infrastructure.Caching;
using HockeyIndex.Api.Infrastructure.RateLimiting;

namespace HockeyIndex.Api.Features.Safety;

public static class SafetyEndpoints
{
    public static IServiceCollection AddSafetyFeature(this IServiceCollection services)
    {
        services.AddScoped<ReportEvent>();
        services.AddScoped<CheckOutboundLink>();
        services.AddScoped<TwilioUsageWebhook>();
        return services;
    }

    public static void Map(RouteGroupBuilder v1)
    {
        ArgumentNullException.ThrowIfNull(v1);

        v1.MapPost("/events/{publicId}/reports", (string publicId, ReportRequest request, HttpContext http, ReportEvent report, CancellationToken ct) =>
                report.HandleAsync(publicId, request, http, ct))
            .RequireRateLimiting(RateLimitPolicies.PublicRead);
        v1.MapGet("/out/check", async (string? u, string? e, HttpResponse response, CheckOutboundLink check, CancellationToken ct) =>
            {
                if (PublicId.IsValid(e ?? ""))
                {
                    response.Headers["Cache-Tag"] = GetPublicEvent.CacheTagFor(e!);
                }

                return TypedResults.Ok(await check.HandleAsync(u, e, ct));
            })
            .CachePublic(60)
            .RequireRateLimiting(RateLimitPolicies.PublicRead);
        v1.MapPost("/internal/twilio-usage", (HttpRequest request, TwilioUsageWebhook webhook, CancellationToken ct) =>
                webhook.HandleAsync(request, ct))
            .AllowAnonymous()
            .DisableAntiforgery();
    }
}
