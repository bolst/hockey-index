using HockeyIndex.Api.Infrastructure.Http;

namespace HockeyIndex.Api.Infrastructure.Caching;

public sealed class CacheHeadersMiddleware(RequestDelegate next)
{
    public const string PrivateNoStore = "private, no-store";

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(static state =>
        {
            var httpContext = (HttpContext)state;
            httpContext.Response.Headers.CacheControl = ResolveCacheControl(httpContext);
            return Task.CompletedTask;
        }, context);

        return next(context);
    }

    internal static string ResolveCacheControl(HttpContext context)
    {
        var metadata = context.GetEndpoint()?.Metadata.GetMetadata<CachePublicMetadata>();
        if (metadata is null || !PublicPaths.Match(context) || !IsCacheableResponse(context))
        {
            return PrivateNoStore;
        }

        return metadata.ToCacheControl();
    }

    // 404 is cacheable so takedowns stay cached as gone; 429 and 5xx must never be cached at the edge.
    private static bool IsCacheableResponse(HttpContext context) =>
        (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
        && context.Response.StatusCode is (>= 200 and < 300) or StatusCodes.Status404NotFound;
}
