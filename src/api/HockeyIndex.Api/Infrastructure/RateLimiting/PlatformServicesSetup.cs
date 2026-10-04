namespace HockeyIndex.Api.Infrastructure.RateLimiting;

public static class PlatformServicesSetup
{
    public static IServiceCollection AddPlatformServices(this IServiceCollection services)
    {
        services.AddScoped<BreakerService>();
        services.AddScoped<ProviderBudget>();
        services.AddOptions<OtpOptions>().BindConfiguration(OtpOptions.SectionName);
        services.AddScoped<OtpLimiter>();
        return services;
    }
}
