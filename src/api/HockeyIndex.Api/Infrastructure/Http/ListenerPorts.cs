using System.Net;

namespace HockeyIndex.Api.Infrastructure.Http;

public static class ListenerPorts
{
    public const int Public = 8080;
    public const int Ops = 8081;
    public const string OpsBindConfigKey = "HI_OPS_BIND";
    private const string DefaultOpsBind = "127.0.0.1";

    public static bool IsOpsListener(this HttpContext context) => context.Connection.LocalPort == Ops;

    public static IWebHostBuilder UseHockeyIndexListeners(this IWebHostBuilder webHost) =>
        webHost.ConfigureKestrel((context, kestrel) =>
        {
            var opsBind = context.Configuration[OpsBindConfigKey];
            kestrel.ListenAnyIP(Public);
            kestrel.Listen(IPAddress.Parse(string.IsNullOrWhiteSpace(opsBind) ? DefaultOpsBind : opsBind), Ops);
        });
}
