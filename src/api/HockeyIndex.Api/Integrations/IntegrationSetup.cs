using HockeyIndex.Api.Integrations.Cloudflare;
using HockeyIndex.Api.Integrations.Fakes;
using HockeyIndex.Api.Integrations.Geocoding;
using HockeyIndex.Api.Integrations.WebRisk;

namespace HockeyIndex.Api.Integrations;

public static class FakeProviders
{
    public const string ConfigKey = "HI_FAKE_PROVIDERS";

    public static bool AreEnabled(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var value = configuration[ConfigKey];
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value == "1";
    }
}

/// <summary>
/// Single switch between real provider clients and in-process fakes. Each provider registers its interface in both
/// <see cref="AddRealIntegrations"/> and <see cref="FakeIntegrations.AddFakeIntegrations"/>.
/// </summary>
public static class IntegrationSetup
{
    public static IServiceCollection AddIntegrations(this IServiceCollection services, IConfiguration configuration) =>
        FakeProviders.AreEnabled(configuration) ? services.AddFakeIntegrations() : services.AddRealIntegrations();

    private static IServiceCollection AddRealIntegrations(this IServiceCollection services) => services
        .AddIdentityProviders()
        .AddWebRiskClient()
        .AddMapboxGeocoder()
        .AddCloudflareCachePurger();
}
