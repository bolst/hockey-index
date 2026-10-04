namespace HockeyIndex.Api.Integrations.Fakes;

/// <summary>In-process provider fakes for development and tests (HI_FAKE_PROVIDERS=true). Refused in Production.</summary>
public static class FakeIntegrations
{
    public static IServiceCollection AddFakeIntegrations(this IServiceCollection services) => services
        .AddFakeIdentityProviders()
        .AddFakeUrlReputation()
        .AddFakeGeocoder()
        .AddFakeCachePurger();
}
