using System.Net;
using System.Net.Sockets;

namespace HockeyIndex.Api.Infrastructure.Http;

public sealed class SsrfBlockedException(string message) : HttpRequestException(message);

/// <summary>
/// Outbound connections to user-supplied hosts. The connect step resolves DNS once, refuses the connection when any
/// resolved address is non-public or the port is not 80/443, and connects to the vetted address, so a second DNS
/// answer (rebinding) can never redirect the socket. Proxies are disabled because they would bypass this check.
/// </summary>
public sealed class SsrfSafeHandler(
    Func<string, CancellationToken, Task<IPAddress[]>> resolve,
    Func<IPEndPoint, CancellationToken, ValueTask<Stream>> connect)
{
    private static readonly IPNetwork[] BlockedNetworks =
    [
        IPNetwork.Parse("0.0.0.0/8"),
        IPNetwork.Parse("10.0.0.0/8"),
        IPNetwork.Parse("100.64.0.0/10"),
        IPNetwork.Parse("127.0.0.0/8"),
        IPNetwork.Parse("169.254.0.0/16"),
        IPNetwork.Parse("172.16.0.0/12"),
        IPNetwork.Parse("192.0.0.0/24"),
        IPNetwork.Parse("192.0.2.0/24"),
        IPNetwork.Parse("192.88.99.0/24"),
        IPNetwork.Parse("192.168.0.0/16"),
        IPNetwork.Parse("198.18.0.0/15"),
        IPNetwork.Parse("198.51.100.0/24"),
        IPNetwork.Parse("203.0.113.0/24"),
        IPNetwork.Parse("224.0.0.0/4"),
        IPNetwork.Parse("240.0.0.0/4"),
        IPNetwork.Parse("::/96"),
        IPNetwork.Parse("::ffff:0:0/96"),
        IPNetwork.Parse("64:ff9b::/96"),
        IPNetwork.Parse("64:ff9b:1::/48"),
        IPNetwork.Parse("100::/64"),
        IPNetwork.Parse("2001::/23"),
        IPNetwork.Parse("2001:db8::/32"),
        IPNetwork.Parse("2002::/16"),
        IPNetwork.Parse("fc00::/7"),
        IPNetwork.Parse("fe80::/10"),
        IPNetwork.Parse("fec0::/10"),
        IPNetwork.Parse("ff00::/8"),
    ];

    private static readonly IPNetwork GlobalUnicastV6 = IPNetwork.Parse("2000::/3");

    public SsrfSafeHandler()
        : this(Dns.GetHostAddressesAsync, ConnectSocketAsync)
    {
    }

    public static bool IsAllowedPort(int port) => port is 80 or 443;

    public static bool IsPublicAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.IsIPv4MappedToIPv6)
        {
            return false;
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => !BlockedNetworks.Any(network => network.Contains(address)),
            AddressFamily.InterNetworkV6 => GlobalUnicastV6.Contains(address) && !BlockedNetworks.Any(network => network.Contains(address)),
            _ => false,
        };
    }

    public SocketsHttpHandler CreateHttpHandler() => new()
    {
        ConnectCallback = (context, cancellationToken) => ConnectAsync(context.DnsEndPoint, cancellationToken),
        AllowAutoRedirect = false,
        UseProxy = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.None,
        MaxResponseHeadersLength = 16,
        PooledConnectionLifetime = TimeSpan.FromMinutes(1),
    };

    public async ValueTask<Stream> ConnectAsync(DnsEndPoint endpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!IsAllowedPort(endpoint.Port))
        {
            throw new SsrfBlockedException($"Port {endpoint.Port} is not allowed.");
        }

        var addresses = IPAddress.TryParse(endpoint.Host, out var literal)
            ? [literal]
            : await resolve(endpoint.Host, cancellationToken);

        if (addresses.Length == 0)
        {
            throw new SsrfBlockedException($"{endpoint.Host} did not resolve.");
        }

        if (!addresses.All(IsPublicAddress))
        {
            throw new SsrfBlockedException($"{endpoint.Host} resolves to a non-public address.");
        }

        return await connect(new IPEndPoint(addresses[0], endpoint.Port), cancellationToken);
    }

    private static async ValueTask<Stream> ConnectSocketAsync(IPEndPoint endpoint, CancellationToken cancellationToken)
    {
        var socket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(endpoint, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
