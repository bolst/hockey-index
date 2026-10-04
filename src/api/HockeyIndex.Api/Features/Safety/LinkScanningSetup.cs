using HockeyIndex.Api.Infrastructure.Http;
using HockeyIndex.Api.Integrations;
using HockeyIndex.Api.Integrations.Fakes;

namespace HockeyIndex.Api.Features.Safety;

public static class LinkScanningSetup
{
    public const string ShortenerUserAgent = "HockeyIndexLinkCheck/1.0 (+https://hockeyindex.com)";

    /// <summary>
    /// Registers <see cref="LinkScanService"/> and its parts. <c>IUrlReputationProvider</c> comes from the integrations switch;
    /// with HI_FAKE_PROVIDERS=true the shortener expander is <see cref="FakeShortenerExpander"/>, which makes no request.
    /// </summary>
    public static IServiceCollection AddLinkScanning(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<LinkScanningOptions>().BindConfiguration(LinkScanningOptions.SectionName);
        services.AddHttpClient<ShortenerExpander>(client =>
            {
                client.Timeout = Timeout.InfiniteTimeSpan;
                client.DefaultRequestHeaders.UserAgent.ParseAdd(ShortenerUserAgent);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SsrfSafeHandler().CreateHttpHandler());
        if (FakeProviders.AreEnabled(configuration))
        {
            services.AddSingleton<IShortenerExpander, FakeShortenerExpander>();
        }
        else
        {
            services.AddTransient<IShortenerExpander>(serviceProvider => serviceProvider.GetRequiredService<ShortenerExpander>());
        }

        services.AddScoped<UrlVerdictCache>();
        services.AddScoped<LinkScanService>();
        return services;
    }
}
