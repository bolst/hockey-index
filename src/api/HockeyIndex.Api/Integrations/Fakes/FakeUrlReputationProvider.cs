using HockeyIndex.Api.Integrations.WebRisk;

namespace HockeyIndex.Api.Integrations.Fakes;

/// <summary>
/// Malicious: Google's test URLs (testsafebrowsing.appspot.com/s/...) and hosts under <c>malicious.test</c>.
/// Failed (provider fault): hosts under <c>unavailable.test</c>. Everything else is safe.
/// </summary>
public sealed class FakeUrlReputationProvider : IUrlReputationProvider
{
    public Task<UrlReputation> LookupAsync(Uri url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);

        var host = url.Host;
        if (IsUnder(host, "unavailable.test"))
        {
            return Task.FromResult(UrlReputation.Failed(isProviderFault: true));
        }

        var isTestThreat = host == "testsafebrowsing.appspot.com" && url.AbsolutePath.StartsWith("/s/", StringComparison.Ordinal);
        return Task.FromResult(isTestThreat || IsUnder(host, "malicious.test")
            ? UrlReputation.Malicious(["SOCIAL_ENGINEERING"], expiresAt: null)
            : UrlReputation.Safe());
    }

    private static bool IsUnder(string host, string domain) =>
        host == domain || host.EndsWith("." + domain, StringComparison.Ordinal);
}

public static class FakeUrlReputationSetup
{
    public static IServiceCollection AddFakeUrlReputation(this IServiceCollection services) =>
        services.AddSingleton<IUrlReputationProvider, FakeUrlReputationProvider>();
}
