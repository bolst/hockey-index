using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace HockeyIndex.Api.Infrastructure.Http;

public sealed class EdgeKeyOptions
{
    public const string SectionName = "Edge";

    public string? Key { get; set; }

    /// <summary>Still accepted during quarterly rotation.</summary>
    public string? PreviousKey { get; set; }
}

/// <summary>Marks requests from the Pages Function (valid X-HI-Edge-Key). Invalid keys are ignored, not rejected.</summary>
public sealed class EdgeCaller
{
    public static readonly EdgeCaller Instance = new();

    private EdgeCaller()
    {
    }
}

public static class EdgeKeyAuth
{
    public const string HeaderName = "X-HI-Edge-Key";

    public static bool IsValid(string? presentedKey, EdgeKeyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrEmpty(presentedKey))
        {
            return false;
        }

        var presented = Encoding.UTF8.GetBytes(presentedKey);
        return Matches(presented, options.Key) | Matches(presented, options.PreviousKey);
    }

    public static bool IsFromEdge(this HttpContext context) => context.Features.Get<EdgeCaller>() is not null;

    private static bool Matches(byte[] presented, string? acceptedKey) =>
        !string.IsNullOrEmpty(acceptedKey) && CryptographicOperations.FixedTimeEquals(presented, Encoding.UTF8.GetBytes(acceptedKey));
}

public sealed class EdgeKeyMiddleware(RequestDelegate next, IOptionsMonitor<EdgeKeyOptions> options)
{
    public Task InvokeAsync(HttpContext context)
    {
        if (EdgeKeyAuth.IsValid(context.Request.Headers[EdgeKeyAuth.HeaderName], options.CurrentValue))
        {
            context.Features.Set(EdgeCaller.Instance);
        }

        return next(context);
    }
}
