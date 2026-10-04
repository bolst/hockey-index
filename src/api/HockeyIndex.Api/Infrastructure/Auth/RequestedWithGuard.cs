using HockeyIndex.Api.Infrastructure.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace HockeyIndex.Api.Infrastructure.Auth;

/// <summary>
/// CSRF defense for cookie-authenticated paths (plan 2.3(f)): every credentialed request carries
/// <c>X-HI-Requested-With: hockey-index</c>, which a cross-site form cannot set without a CORS preflight,
/// and unsafe methods with an <c>Origin</c> header must come from the SPA origin.
/// </summary>
public sealed class RequestedWithGuard(RequestDelegate next, IOptions<SpaCorsOptions> options, IProblemDetailsService problemDetails)
{
    public const string HeaderName = "X-HI-Requested-With";
    public const string HeaderValue = "hockey-index";

    public Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        if (!CredentialedPaths.Match(request.Path) || HttpMethods.IsOptions(request.Method))
        {
            return next(context);
        }

        if (!string.Equals(request.Headers[HeaderName], HeaderValue, StringComparison.Ordinal))
        {
            return RejectAsync(context, $"The {HeaderName} header is required.");
        }

        var origin = request.Headers.Origin.ToString();
        if (IsUnsafe(request.Method) && origin.Length > 0 && !string.Equals(origin, options.Value.SpaOrigin, StringComparison.Ordinal))
        {
            return RejectAsync(context, "Cross-origin request refused.");
        }

        return next(context);
    }

    private static bool IsUnsafe(string method) =>
        !(HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method));

    private async Task RejectAsync(HttpContext context, string detail)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Forbidden",
                Detail = detail,
                Extensions = { ["code"] = "csrf_rejected" },
            },
        });
    }
}
