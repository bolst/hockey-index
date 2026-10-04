using HockeyIndex.Api.Integrations.Email;
using HockeyIndex.Api.Integrations.Fakes;
using HockeyIndex.Api.Integrations.Turnstile;
using HockeyIndex.Api.Integrations.Twilio;

namespace HockeyIndex.Api.Integrations;

/// <summary>Twilio Verify + Lookup, Turnstile, and Postmark: the providers behind host sign-in.</summary>
public static class IdentityProviderSetup
{
    private static readonly TimeSpan ProviderTimeout = TimeSpan.FromSeconds(10);

    public static IServiceCollection AddIdentityProviders(this IServiceCollection services)
    {
        services.AddIdentityProviderOptions();
        services.AddHttpClient<ISmsVerifier, TwilioVerifyClient>(client => client.Timeout = ProviderTimeout);
        services.AddHttpClient<IPhoneLineTypeLookup, TwilioLookupClient>(client => client.Timeout = ProviderTimeout);
        services.AddHttpClient<ITurnstileVerifier, TurnstileVerifier>(client => client.Timeout = ProviderTimeout);
        services.AddHttpClient<IEmailSender, PostmarkEmailSender>(client => client.Timeout = ProviderTimeout);
        return services;
    }

    public static IServiceCollection AddFakeIdentityProviders(this IServiceCollection services)
    {
        services.AddIdentityProviderOptions();
        services.AddSingleton<FakeSmsVerifier>();
        services.AddSingleton<ISmsVerifier>(serviceProvider => serviceProvider.GetRequiredService<FakeSmsVerifier>());
        services.AddSingleton<FakePhoneLineTypeLookup>();
        services.AddSingleton<IPhoneLineTypeLookup>(serviceProvider => serviceProvider.GetRequiredService<FakePhoneLineTypeLookup>());
        services.AddSingleton<FakeTurnstileVerifier>();
        services.AddSingleton<ITurnstileVerifier>(serviceProvider => serviceProvider.GetRequiredService<FakeTurnstileVerifier>());
        services.AddSingleton<FakeEmailSender>();
        services.AddSingleton<IEmailSender>(serviceProvider => serviceProvider.GetRequiredService<FakeEmailSender>());
        return services;
    }

    private static void AddIdentityProviderOptions(this IServiceCollection services)
    {
        services.AddOptions<TwilioOptions>().BindConfiguration(TwilioOptions.SectionName);
        services.AddOptions<TurnstileOptions>().BindConfiguration(TurnstileOptions.SectionName);
        services.AddOptions<PostmarkOptions>().BindConfiguration(PostmarkOptions.SectionName);
    }
}
