using HockeyIndex.Api.Features.Admin;
using HockeyIndex.Api.Integrations;

namespace HockeyIndex.Api.Infrastructure.Observability;

public static class StartupGuards
{
    public static void EnsureSafeConfiguration(IHostEnvironment environment, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(environment);

        if (environment.IsProduction() && FakeProviders.AreEnabled(configuration))
        {
            throw new InvalidOperationException($"{FakeProviders.ConfigKey}=true is refused in Production.");
        }

        if (environment.IsProduction() && configuration.GetSection($"{AdminOptions.SectionName}:{nameof(AdminOptions.BootstrapPhones)}").GetChildren().Any())
        {
            throw new InvalidOperationException($"{AdminOptions.SectionName}:{nameof(AdminOptions.BootstrapPhones)} is refused in Production.");
        }
    }
}
