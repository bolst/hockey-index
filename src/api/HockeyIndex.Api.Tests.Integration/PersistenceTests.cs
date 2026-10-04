using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Infrastructure.RateLimiting;
using HockeyIndex.Api.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;

namespace HockeyIndex.Api.Tests.Integration;

public sealed class PersistenceTests(PostgisFixture postgis) : IntegrationTest(postgis)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Data_protection_keys_survive_a_restart()
    {
        var protectedPayload = Factory.Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("tests").Protect("secret");
        await Factory.DisposeAsync();

        var restarted = CreateFactory();
        var unprotected = restarted.Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("tests").Unprotect(protectedPayload);

        Assert.Equal("secret", unprotected);
        await using var db = Postgis.CreateDbContext();
        Assert.NotEmpty(await db.DataProtectionKeys.ToListAsync(Ct));
    }

    [Fact]
    public async Task Per_host_cap_does_not_block_other_hosts_and_refusals_are_not_counted()
    {
        var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["Providers:Caps:webrisk:Global"] = "10",
            ["Providers:Caps:webrisk:PerHost"] = "2",
        });
        var hostA = Guid.CreateVersion7();
        var hostB = Guid.CreateVersion7();

        var hostAResults = new List<bool>();
        for (var i = 0; i < 3; i++)
        {
            hostAResults.Add(await ConsumeAsync(factory, ProviderNames.WebRisk, hostA));
        }

        var hostBResult = await ConsumeAsync(factory, ProviderNames.WebRisk, hostB);

        Assert.Equal([true, true, false], hostAResults);
        Assert.True(hostBResult);
        Assert.Equal(2, await CallsAsync(ProviderNames.WebRisk, hostA));
        Assert.Equal(1, await CallsAsync(ProviderNames.WebRisk, hostB));
        Assert.Equal(3, await CallsAsync(ProviderNames.WebRisk, ProviderUsage.GlobalHostId));
    }

    [Fact]
    public async Task Global_cap_refusal_rolls_back_the_host_increment()
    {
        var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["Providers:Caps:webrisk:Global"] = "2",
            ["Providers:Caps:webrisk:PerHost"] = "5",
        });
        var hostA = Guid.CreateVersion7();
        var hostB = Guid.CreateVersion7();

        Assert.True(await ConsumeAsync(factory, ProviderNames.WebRisk, hostA));
        Assert.True(await ConsumeAsync(factory, ProviderNames.WebRisk, hostA));
        Assert.False(await ConsumeAsync(factory, ProviderNames.WebRisk, hostB));

        Assert.Equal(0, await CallsAsync(ProviderNames.WebRisk, hostB));
        Assert.Equal(2, await CallsAsync(ProviderNames.WebRisk, ProviderUsage.GlobalHostId));
    }

    [Fact]
    public async Task Budget_resets_on_a_new_utc_day()
    {
        var factory = CreateFactory(new Dictionary<string, string?> { ["Providers:Caps:twilio_verify:Global"] = "1" });

        Assert.True(await ConsumeAsync(factory, ProviderNames.TwilioVerify, null));
        Assert.False(await ConsumeAsync(factory, ProviderNames.TwilioVerify, null));
        factory.Clock.Advance(Duration.FromDays(1));

        Assert.True(await ConsumeAsync(factory, ProviderNames.TwilioVerify, null));
    }

    [Fact]
    public async Task Breaker_state_persists_across_scopes_and_restarts()
    {
        var openUntil = Factory.Clock.GetCurrentInstant().Plus(Duration.FromMinutes(10)).ToDateTimeOffset();
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<BreakerService>()
                .OpenAsync(BreakerNames.OtpSms, openUntil, "test", BreakerOpener.Admin, Ct);
        }

        Assert.True(await IsOpenAsync(Factory, BreakerNames.OtpSms));
        Assert.False(await IsOpenAsync(Factory, BreakerNames.Mapbox));
        Assert.True(await IsOpenAsync(CreateFactory(), BreakerNames.OtpSms));

        Factory.Clock.Advance(Duration.FromMinutes(11));
        Assert.False(await IsOpenAsync(Factory, BreakerNames.OtpSms));
    }

    [Fact]
    public async Task Closed_breaker_reports_closed()
    {
        var openUntil = Factory.Clock.GetCurrentInstant().Plus(Duration.FromHours(1)).ToDateTimeOffset();
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var breakers = scope.ServiceProvider.GetRequiredService<BreakerService>();
            await breakers.OpenAsync(BreakerNames.Mapbox, openUntil, "test", BreakerOpener.Auto, Ct);
            await breakers.CloseAsync(BreakerNames.Mapbox, Ct);
        }

        Assert.False(await IsOpenAsync(Factory, BreakerNames.Mapbox));
    }

    private static async Task<bool> ConsumeAsync(ApiFactory factory, string provider, Guid? hostId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ProviderBudget>().TryConsumeAsync(provider, hostId, Ct);
    }

    private static async Task<bool> IsOpenAsync(ApiFactory factory, string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<BreakerService>().IsOpenAsync(name, Ct);
    }

    private async Task<int> CallsAsync(string provider, Guid hostId)
    {
        await using var db = Postgis.CreateDbContext();
        return await db.ProviderUsage
            .Where(u => u.Provider == provider && u.HostId == hostId)
            .Select(u => u.Calls)
            .SingleOrDefaultAsync(Ct);
    }
}
