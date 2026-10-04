using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace HockeyIndex.Api.Infrastructure.Http;

public sealed class SpaCorsOptions
{
    public const string SectionName = "Cors";

    public string SpaOrigin { get; set; } = "https://hockeyindex.com";
}

/// <summary>
/// Public paths: ACAO * and never credentials. Credentialed paths: exact SPA origin, Allow-Credentials, Vary: Origin.
/// Everything else gets no CORS headers.
/// </summary>
public sealed class PublicCorsMiddleware(RequestDelegate next, IOptions<SpaCorsOptions> options)
{
    private const string PublicMethods = "GET, POST";
    private const string PublicHeaders = "Content-Type";
    private const string CredentialedMethods = "GET, POST, PUT, DELETE";
    private const string CredentialedHeaders = "Content-Type, X-HI-Requested-With, If-Match";
    private const string PreflightMaxAgeSeconds = "600";

    public Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        if (PublicPaths.Match(path))
        {
            return HandlePublic(context);
        }

        if (CredentialedPaths.Match(path))
        {
            return HandleCredentialed(context);
        }

        return next(context);
    }

    private Task HandlePublic(HttpContext context)
    {
        context.Response.OnStarting(static state =>
        {
            var headers = ((HttpContext)state).Response.Headers;
            headers.AccessControlAllowOrigin = "*";
            headers.Remove(HeaderNames.AccessControlAllowCredentials);
            return Task.CompletedTask;
        }, context);

        return IsPreflight(context.Request)
            ? RespondToPreflight(context, PublicMethods, PublicHeaders)
            : next(context);
    }

    private Task HandleCredentialed(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        var originAllowed = string.Equals(origin, options.Value.SpaOrigin, StringComparison.Ordinal);

        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.Append(HeaderNames.Vary, HeaderNames.Origin);
            if (originAllowed)
            {
                headers.AccessControlAllowOrigin = origin;
                headers.AccessControlAllowCredentials = "true";
                headers.AccessControlExposeHeaders = HeaderNames.ETag;
            }

            return Task.CompletedTask;
        });

        if (!IsPreflight(context.Request))
        {
            return next(context);
        }

        return originAllowed
            ? RespondToPreflight(context, CredentialedMethods, CredentialedHeaders)
            : RespondEmpty(context);
    }

    private static bool IsPreflight(HttpRequest request) =>
        HttpMethods.IsOptions(request.Method) && request.Headers.ContainsKey(HeaderNames.AccessControlRequestMethod);

    private static Task RespondToPreflight(HttpContext context, string methods, string headers)
    {
        context.Response.Headers.AccessControlAllowMethods = methods;
        context.Response.Headers.AccessControlAllowHeaders = headers;
        context.Response.Headers.AccessControlMaxAge = PreflightMaxAgeSeconds;
        return RespondEmpty(context);
    }

    private static Task RespondEmpty(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status204NoContent;
        return Task.CompletedTask;
    }
}
