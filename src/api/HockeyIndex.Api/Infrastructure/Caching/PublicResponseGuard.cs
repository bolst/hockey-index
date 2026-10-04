using HockeyIndex.Api.Infrastructure.Http;
using HockeyIndex.Api.Infrastructure.Observability;
using Microsoft.Net.Http.Headers;

namespace HockeyIndex.Api.Infrastructure.Caching;

/// <summary>
/// Defense in depth: a public (edge-cached) response must never carry Set-Cookie.
/// Register first so its OnStarting callback runs last, after anything else that may add cookies.
/// </summary>
public sealed partial class PublicResponseGuard(RequestDelegate next, HockeyIndexMetrics metrics, ILogger<PublicResponseGuard> logger)
{
    public Task InvokeAsync(HttpContext context)
    {
        if (PublicPaths.Match(context))
        {
            context.Response.OnStarting(StripSetCookie, context);
        }

        return next(context);
    }

    private Task StripSetCookie(object state)
    {
        var context = (HttpContext)state;
        if (context.Response.Headers.Remove(HeaderNames.SetCookie))
        {
            metrics.RecordPublicSetCookieStripped();
            LogSetCookieStripped(logger, context.Request.Path);
        }

        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stripped Set-Cookie from public response {Path}")]
    private static partial void LogSetCookieStripped(ILogger logger, PathString path);
}
