using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace HockeyIndex.Api.Infrastructure.Http;

/// <summary>Client address as seen by Cloudflare. IPv6 is reduced to its /64 so one subscriber maps to one key.</summary>
public sealed record ClientIp(IPAddress Address, ReadOnlyMemory<byte> Key)
{
    public string KeyHex => Convert.ToHexString(Key.Span);
}

public sealed class ClientIpOptions
{
    public const string SectionName = "ClientIp";

    /// <summary>Local development only: fall back to the socket address when CF-Connecting-IP is absent.</summary>
    public bool AllowMissingHeader { get; set; }
}

public static class ClientIpResolver
{
    public const string HeaderName = "CF-Connecting-IP";
    private const int Ipv6PrefixBytes = 8;

    public static bool TryResolve(string? headerValue, [NotNullWhen(true)] out ClientIp? clientIp)
    {
        clientIp = null;
        if (string.IsNullOrWhiteSpace(headerValue) || !IPAddress.TryParse(headerValue.Trim(), out var address))
        {
            return false;
        }

        clientIp = FromAddress(address);
        return true;
    }

    public static ClientIp FromAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return new ClientIp(address, address.GetAddressBytes());
        }

        var network = new byte[16];
        address.GetAddressBytes().AsSpan(0, Ipv6PrefixBytes).CopyTo(network);
        return new ClientIp(new IPAddress(network), network.AsMemory(0, Ipv6PrefixBytes));
    }

    public static ClientIp GetClientIp(this HttpContext context) =>
        context.Features.Get<ClientIp>() ?? throw new InvalidOperationException("ClientIpMiddleware has not run.");
}

public sealed class ClientIpMiddleware(RequestDelegate next, IOptions<ClientIpOptions> options, IProblemDetailsService problemDetails)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var headerValue = context.Request.Headers[ClientIpResolver.HeaderName].ToString();

        if (ClientIpResolver.TryResolve(headerValue, out var clientIp))
        {
            context.Features.Set(clientIp);
            await next(context);
            return;
        }

        var headerMissing = string.IsNullOrEmpty(headerValue);
        if (headerMissing && (context.IsOpsListener() || options.Value.AllowMissingHeader))
        {
            context.Features.Set(ClientIpResolver.FromAddress(context.Connection.RemoteIpAddress ?? IPAddress.Loopback));
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Client address unavailable",
                Detail = $"A valid {ClientIpResolver.HeaderName} header is required.",
            },
        });
    }
}
