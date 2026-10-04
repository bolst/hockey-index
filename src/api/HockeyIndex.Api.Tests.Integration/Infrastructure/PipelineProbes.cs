using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace HockeyIndex.Api.Tests.Integration.Infrastructure;

/// <summary>Simulates a handler that wrongly sets a cookie, ahead of the real pipeline.</summary>
public sealed class SetCookieInjector : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            context.Response.Headers.Append(HeaderNames.SetCookie, "leak=1; Path=/");
            return nextMiddleware(context);
        });
        next(app);
    };
}

/// <summary>Records, per request path, whether the authentication middleware ran.</summary>
public sealed class AuthenticationProbe : IStartupFilter
{
    public ConcurrentDictionary<string, bool> AuthenticationRan { get; } = new();

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, nextMiddleware) =>
        {
            await nextMiddleware(context);
            AuthenticationRan[context.Request.Path] = context.Features.Get<IAuthenticationFeature>() is not null;
        });
        next(app);
    };
}
