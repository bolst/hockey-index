using System.Net;
using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Integrations.Fakes;
using HockeyIndex.Api.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static HockeyIndex.Api.Tests.Integration.Infrastructure.AuthTestClient;
using Host = HockeyIndex.Api.Domain.Host;

namespace HockeyIndex.Api.Tests.Integration;

public sealed class PhoneAuthTests(PostgisFixture postgis) : IntegrationTest(postgis)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private FakeSmsVerifier Sms(ApiFactory? factory = null) => (factory ?? Factory).Services.GetRequiredService<FakeSmsVerifier>();

    private FakePhoneLineTypeLookup Lookup => Factory.Services.GetRequiredService<FakePhoneLineTypeLookup>();

    [Fact]
    public async Task Failed_turnstile_sends_nothing_and_is_logged()
    {
        using var client = Factory.CreateHostClient();

        using var response = await client.StartPhoneAsync(Phone(1), turnstileToken: "fail-bot");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("turnstile_failed", await ProblemCodeAsync(response));
        Assert.Empty(Sms().StartedNumbers);
        Assert.Equal(0, Lookup.LookupCount);
        Assert.Equal(OtpOutcomes.TurnstileFailed, await SingleOutcomeAsync());
    }

    [Fact]
    public async Task Fourth_send_per_phone_in_an_hour_is_429_without_twilio()
    {
        for (var i = 0; i < 3; i++)
        {
            using var client = Factory.CreateHostClient($"198.51.100.{i + 1}");
            using var ok = await client.StartPhoneAsync(Phone(1));
            Assert.Equal(HttpStatusCode.Accepted, ok.StatusCode);
        }

        using var fourthClient = Factory.CreateHostClient("198.51.100.99");
        using var fourth = await fourthClient.StartPhoneAsync(Phone(1));

        Assert.Equal(HttpStatusCode.TooManyRequests, fourth.StatusCode);
        Assert.Equal("rate_limited", await ProblemCodeAsync(fourth));
        Assert.Equal(3, Sms().StartedNumbers.Count);

        Factory.Clock.AdvanceMinutes(61);
        using var nextHour = await fourthClient.StartPhoneAsync(Phone(1));
        Assert.Equal(HttpStatusCode.Accepted, nextHour.StatusCode);
    }

    [Fact]
    public async Task Eleventh_send_per_ip_in_an_hour_is_429()
    {
        using var client = Factory.CreateHostClient();
        for (var i = 0; i < 10; i++)
        {
            using var ok = await client.StartPhoneAsync(Phone(i));
            Assert.Equal(HttpStatusCode.Accepted, ok.StatusCode);
        }

        using var eleventh = await client.StartPhoneAsync(Phone(10));

        Assert.Equal(HttpStatusCode.TooManyRequests, eleventh.StatusCode);
        Assert.Equal(10, Sms().StartedNumbers.Count);
    }

    [Fact]
    public async Task Npa_nxx_cap_limits_sends_across_ips()
    {
        var factory = CreateFactory(new Dictionary<string, string?> { ["Otp:SmsPerNpaNxxPerHour"] = "2" });
        for (var i = 0; i < 2; i++)
        {
            using var client = factory.CreateHostClient($"198.51.100.{i + 1}");
            using var ok = await client.StartPhoneAsync(Phone(i));
            Assert.Equal(HttpStatusCode.Accepted, ok.StatusCode);
        }

        using var otherIp = factory.CreateHostClient("198.51.100.50");
        using var limited = await otherIp.StartPhoneAsync(Phone(5));
        using var otherExchange = await otherIp.StartPhoneAsync("+12065551234");

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, otherExchange.StatusCode);
        Assert.Equal(3, Sms(factory).StartedNumbers.Count);
    }

    [Theory]
    [InlineData("+442071838750")]
    [InlineData("+18765550123")]
    [InlineData("+525512345678")]
    public async Task Non_us_ca_numbers_are_rejected_before_twilio(string phone)
    {
        using var client = Factory.CreateHostClient();

        using var response = await client.StartPhoneAsync(phone);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("unsupported_region", await ProblemCodeAsync(response));
        Assert.Empty(Sms().StartedNumbers);
        Assert.Equal(0, Lookup.LookupCount);
        Assert.Equal(OtpOutcomes.RejectedRegion, await SingleOutcomeAsync());
    }

    [Fact]
    public async Task Canadian_mobile_is_accepted()
    {
        using var client = Factory.CreateHostClient();

        using var response = await client.StartPhoneAsync("+14165550199");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(["+14165550199"], Sms().StartedNumbers);
    }

    [Fact]
    public async Task Toll_free_is_rejected_without_lookup()
    {
        using var client = Factory.CreateHostClient();

        using var response = await client.StartPhoneAsync("+18005550100");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("unsupported_line_type", await ProblemCodeAsync(response));
        Assert.Equal(0, Lookup.LookupCount);
        Assert.Empty(Sms().StartedNumbers);
    }

    [Theory]
    [InlineData("+12064120001")]
    [InlineData("+12064120003")]
    public async Task Landline_and_toll_free_lookup_results_are_rejected_before_verify(string phone)
    {
        using var client = Factory.CreateHostClient();

        using var response = await client.StartPhoneAsync(phone);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("unsupported_line_type", await ProblemCodeAsync(response));
        Assert.Equal(1, Lookup.LookupCount);
        Assert.Empty(Sms().StartedNumbers);
        Assert.Equal(OtpOutcomes.RejectedLineType, await SingleOutcomeAsync());
    }

    [Theory]
    [InlineData("+12064120002")]
    [InlineData("+12064120004")]
    public async Task Voip_numbers_are_allowed_and_flagged(string phone)
    {
        using var client = Factory.CreateHostClient();

        await client.SignInWithPhoneAsync(phone);
        using var me = await client.GetMeAsync();

        Assert.True((await ReadJsonAsync(me)).GetProperty("isVoip").GetBoolean());
        await using var db = Postgis.CreateDbContext();
        var host = await db.Users.SingleAsync(Ct);
        Assert.True(host.IsVoip);
        Assert.NotNull(host.PhoneLineType);
    }

    [Fact]
    public async Task Line_type_is_looked_up_once_per_cache_window()
    {
        using var client = Factory.CreateHostClient();

        using (await client.StartPhoneAsync(Phone(1)))
        using (await client.StartPhoneAsync(Phone(1)))
        {
            Assert.Equal(1, Lookup.LookupCount);
            Assert.Equal(2, Sms().StartedNumbers.Count);
        }
    }

    [Fact]
    public async Task Blocklisted_phone_gets_generic_202_and_no_send()
    {
        await using (var db = Postgis.CreateDbContext())
        {
            db.Blocklist.Add(new BlocklistEntry { Kind = BlocklistKind.Phone, Value = Phone(1), Reason = "abuse", CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(Ct);
        }

        using var client = Factory.CreateHostClient();
        using var response = await client.StartPhoneAsync(Phone(1));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(Sms().StartedNumbers);
        Assert.Equal(OtpOutcomes.RejectedBlocklist, await SingleOutcomeAsync());
    }

    [Fact]
    public async Task Banned_host_gets_generic_202_and_no_send()
    {
        using var client = Factory.CreateHostClient();
        var hostId = await client.SignInWithPhoneAsync(Phone(1));
        await BanAsync(hostId);

        using var start = await client.StartPhoneAsync(Phone(1));
        using var verify = await client.VerifyPhoneAsync(Phone(1));

        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        Assert.Single(Sms().StartedNumbers);
        Assert.Equal(HttpStatusCode.BadRequest, verify.StatusCode);
    }

    [Fact]
    public async Task Invite_only_mode_requires_an_invite_for_new_phones()
    {
        var factory = CreateFactory(new Dictionary<string, string?> { ["Signup:Mode"] = "invite_only" });
        await using (var db = Postgis.CreateDbContext())
        {
            db.SignupInvites.Add(new SignupInvite { PhoneE164 = Phone(2), ExpiresAt = factory.Clock.GetCurrentInstant().ToDateTimeOffset().AddDays(7) });
            await db.SaveChangesAsync(Ct);
        }

        using var client = factory.CreateHostClient();
        using var uninvited = await client.StartPhoneAsync(Phone(1));
        Assert.Equal(HttpStatusCode.Forbidden, uninvited.StatusCode);
        Assert.Equal("invite_required", await ProblemCodeAsync(uninvited));
        Assert.Empty(Sms(factory).StartedNumbers);

        await client.SignInWithPhoneAsync(Phone(2));
        await using (var db = Postgis.CreateDbContext())
        {
            Assert.NotNull((await db.SignupInvites.SingleAsync(Ct)).UsedAt);
        }

        using var existingHost = factory.CreateHostClient("198.51.100.7");
        using var again = await existingHost.StartPhoneAsync(Phone(2));
        Assert.Equal(HttpStatusCode.Accepted, again.StatusCode);
    }

    [Fact]
    public async Task Open_breaker_refuses_sends()
    {
        await using (var db = Postgis.CreateDbContext())
        {
            db.Breakers.Add(new Breaker
            {
                Name = BreakerNames.OtpSms,
                OpenUntil = Factory.Clock.GetCurrentInstant().ToDateTimeOffset().AddMinutes(10),
                Reason = "test",
                OpenedBy = BreakerOpener.Admin,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(Ct);
        }

        using var client = Factory.CreateHostClient();
        using var response = await client.StartPhoneAsync(Phone(1));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("sms_unavailable", await ProblemCodeAsync(response));
        Assert.Empty(Sms().StartedNumbers);
    }

    [Fact]
    public async Task Low_conversion_opens_the_breaker()
    {
        var factory = CreateFactory(new Dictionary<string, string?> { ["Otp:BreakerMinSendsForConversion"] = "3" });
        using var client = factory.CreateHostClient();
        for (var i = 0; i < 3; i++)
        {
            using var ok = await client.StartPhoneAsync(Phone(i));
            Assert.Equal(HttpStatusCode.Accepted, ok.StatusCode);
        }

        using var refused = await client.StartPhoneAsync(Phone(3));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
        Assert.Equal(3, Sms(factory).StartedNumbers.Count);
        await using var db = Postgis.CreateDbContext();
        var breaker = await db.Breakers.SingleAsync(b => b.Name == BreakerNames.OtpSms, Ct);
        Assert.Equal(BreakerOpener.Auto, breaker.OpenedBy);
        Assert.StartsWith("conversion:", breaker.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Exhausted_verify_budget_refuses_sends()
    {
        var factory = CreateFactory(new Dictionary<string, string?> { ["Providers:Caps:twilio_verify:Global"] = "1" });
        using var client = factory.CreateHostClient();

        using var first = await client.StartPhoneAsync(Phone(1));
        using var second = await client.StartPhoneAsync(Phone(2));

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, second.StatusCode);
        Assert.Single(Sms(factory).StartedNumbers);
    }

    [Fact]
    public async Task Concurrent_sends_for_one_phone_reach_twilio_exactly_three_times()
    {
        var clients = Enumerable.Range(0, 20).Select(i => Factory.CreateHostClient($"198.51.100.{i + 1}")).ToList();

        var responses = await Task.WhenAll(clients.Select(client => client.StartPhoneAsync(Phone(1))));

        Assert.Equal(3, Sms().StartedNumbers.Count);
        Assert.Equal(3, responses.Count(response => response.StatusCode == HttpStatusCode.Accepted));
        Assert.Equal(17, responses.Count(response => response.StatusCode == HttpStatusCode.TooManyRequests));
        DisposeAll(responses, clients);
    }

    [Fact]
    public async Task Concurrent_sends_from_one_ip_reach_twilio_exactly_ten_times()
    {
        using var client = Factory.CreateHostClient();

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => client.StartPhoneAsync(Phone(i))));

        Assert.Equal(10, Sms().StartedNumbers.Count);
        Assert.Equal(10, responses.Count(response => response.StatusCode == HttpStatusCode.Accepted));
        DisposeAll(responses, []);
    }

    [Fact]
    public async Task Verify_sets_the_session_cookie_with_required_attributes()
    {
        using var client = Factory.CreateHostClient();
        using (await client.StartPhoneAsync(Phone(1)))
        {
        }

        using var response = await client.VerifyPhoneAsync(Phone(1));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = Microsoft.Net.Http.Headers.SetCookieHeaderValue.Parse(Assert.Single(response.Headers.GetValues("Set-Cookie")));
        Assert.Equal("__Host-hi_auth", cookie.Name.Value);
        Assert.True(cookie.Secure);
        Assert.True(cookie.HttpOnly);
        Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Lax, cookie.SameSite);
        Assert.Equal("/", cookie.Path.Value);
        Assert.Null(cookie.Domain.Value);
        Assert.Equal(Factory.Clock.GetCurrentInstant().ToDateTimeOffset().AddDays(30), cookie.Expires);
    }

    [Fact]
    public async Task Wrong_code_is_rejected_and_checks_are_limited()
    {
        using var client = Factory.CreateHostClient();
        using (await client.StartPhoneAsync(Phone(1)))
        {
        }

        for (var i = 0; i < 5; i++)
        {
            using var wrong = await client.VerifyPhoneAsync(Phone(1), "000000");
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        }

        using var sixth = await client.VerifyPhoneAsync(Phone(1));
        Assert.Equal(HttpStatusCode.TooManyRequests, sixth.StatusCode);
    }

    [Fact]
    public async Task One_phone_maps_to_one_host()
    {
        using var client = Factory.CreateHostClient();
        var first = await client.SignInWithPhoneAsync(Phone(1));
        var second = await client.SignInWithPhoneAsync(Phone(1));

        Assert.Equal(first, second);
        await using var db = Postgis.CreateDbContext();
        Assert.Equal(1, await db.Users.CountAsync(Ct));

        db.Users.Add(new Host
        {
            Id = Guid.CreateVersion7(),
            UserName = "duplicate",
            NormalizedUserName = "DUPLICATE",
            PhoneNumber = Phone(1),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        Assert.Contains("ux_hosts_phone_number", exception.InnerException?.Message, StringComparison.Ordinal);
    }

    private async Task<string> SingleOutcomeAsync()
    {
        await using var db = Postgis.CreateDbContext();
        return (await db.OtpLog.SingleAsync(Ct)).Outcome;
    }

    private async Task BanAsync(Guid hostId)
    {
        await using var db = Postgis.CreateDbContext();
        await db.Users.Where(host => host.Id == hostId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(host => host.Status, HostStatus.Banned), Ct);
    }

    private static void DisposeAll(IEnumerable<HttpResponseMessage> responses, IEnumerable<HttpClient> clients)
    {
        foreach (var response in responses)
        {
            response.Dispose();
        }

        foreach (var client in clients)
        {
            client.Dispose();
        }
    }
}
