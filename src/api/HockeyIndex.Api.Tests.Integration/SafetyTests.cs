using System.Net;
using System.Net.Http.Json;
using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Features.Jobs;
using HockeyIndex.Api.Features.Safety;
using HockeyIndex.Api.Integrations.Fakes;
using HockeyIndex.Api.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using NodaTime;
using static HockeyIndex.Api.Tests.Integration.Infrastructure.AuthTestClient;
using static HockeyIndex.Api.Tests.Integration.Infrastructure.EventTestSupport;

namespace HockeyIndex.Api.Tests.Integration;

/// <summary>A shortener whose destination can change between scans, as real ones do.</summary>
public sealed class SwitchableShortenerExpander(IOptions<LinkScanningOptions> options) : IShortenerExpander
{
    private readonly FakeShortenerExpander inner = new(options);

    public bool PointsAtMalware { get; set; }

    public bool IsShortener(Uri url) => inner.IsShortener(url);

    public Task<ShortenerExpansion> ExpandAsync(Uri url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (!PointsAtMalware || !IsShortener(url))
        {
            return inner.ExpandAsync(url, cancellationToken);
        }

        var finalUrl = new Uri($"https://malicious.test{url.AbsolutePath}");
        return Task.FromResult(new ShortenerExpansion(ExpansionOutcome.Resolved, finalUrl, [url, finalUrl]));
    }

    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<SwitchableShortenerExpander>();
        services.AddSingleton<IShortenerExpander>(provider => provider.GetRequiredService<SwitchableShortenerExpander>());
    }
}

public sealed class SafetyTests(PostgisFixture postgis) : IntegrationTest(postgis)
{
    private const string TwilioToken = "twilio-test-auth-token";
    private const string TwilioCallbackUrl = "https://api.hockeyindex.test/v1/internal/twilio-usage";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateTimeOffset Now(ApiFactory factory) => factory.Clock.GetCurrentInstant().ToDateTimeOffset();

    [Fact]
    public async Task Concurrent_reports_from_distinct_reporters_hide_the_event_exactly_once()
    {
        var (factory, publicId, eventId) = await PublishedEventAsync();
        var clients = Enumerable.Range(1, 8).Select(n => factory.CreatePublicClient($"198.51.100.{n}")).ToList();

        var responses = await Task.WhenAll(clients.Select(client => ReportAsync(client, publicId)));

        Assert.All(responses, response => Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NoContent, HttpStatusCode.NotFound }));
        Assert.True(responses.Count(response => response.StatusCode == HttpStatusCode.NoContent) >= ReportEvent.AutoHideThreshold);
        await using var db = Postgis.CreateDbContext();
        var evt = await LoadAsync(db, eventId);
        Assert.Equal(EventStatus.Hidden, evt.Status);
        Assert.Equal(HiddenReason.Reports, evt.HiddenReason);
        Assert.Equal(1, await db.EventStatusChanges.CountAsync(change => change.EventId == eventId && change.ToStatus == EventStatus.Hidden, Ct));
        DisposeAll(responses);
        DisposeAll(clients);
    }

    [Fact]
    public async Task Reports_from_one_IPv6_64_count_as_one_reporter_and_two_reporters_do_not_hide()
    {
        var (factory, publicId, eventId) = await PublishedEventAsync();
        using var first = factory.CreatePublicClient("2001:db8:1:2::1");
        using var sameSubnet = factory.CreatePublicClient("2001:db8:1:2:ffff::9");
        using var otherSubnet = factory.CreatePublicClient("2001:db8:1:3::1");

        foreach (var client in new[] { first, sameSubnet, first, otherSubnet })
        {
            using var response = await ReportAsync(client, publicId);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        await using var db = Postgis.CreateDbContext();
        Assert.Equal(2, await db.Reports.CountAsync(report => report.EventId == eventId, Ct));
        Assert.Equal(EventStatus.Published, (await LoadAsync(db, eventId)).Status);
    }

    [Fact]
    public async Task Reports_require_turnstile_and_a_known_reason()
    {
        var (factory, publicId, _) = await PublishedEventAsync();
        using var client = factory.CreatePublicClient();

        using var failedTurnstile = await ReportAsync(client, publicId, turnstileToken: "fail-me");
        using var badReason = await ReportAsync(client, publicId, reason: "boring");
        using var unknownEvent = await ReportAsync(client, "aaaaaaaaaaaa");

        Assert.Equal(HttpStatusCode.BadRequest, failedTurnstile.StatusCode);
        Assert.Equal("turnstile_failed", await ProblemCodeAsync(failedTurnstile));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badReason.StatusCode);
        Assert.Equal("invalid_report", await ProblemCodeAsync(badReason));
        Assert.Equal(HttpStatusCode.NotFound, unknownEvent.StatusCode);
        await using var db = Postgis.CreateDbContext();
        Assert.False(await db.Reports.AnyAsync(Ct));
    }

    [Fact]
    public async Task A_reporter_is_limited_to_five_reports_an_hour()
    {
        var factory = CreateFactory();
        using var host = factory.CreateHostClient();
        var hostId = await host.SignInWithPhoneAsync(Phone(1));
        await using var db = Postgis.CreateDbContext();
        var venue = await SeedVenueAsync(db, Now(factory));
        var ids = new List<string>();
        for (var i = 0; i < ReportEvent.MaxReportsPerHour + 1; i++)
        {
            var draft = await SeedDraftAsync(db, hostId, venue, Now(factory));
            await ForceStatusAsync(db, draft.Id, "published");
            ids.Add(draft.PublicId);
        }

        using var client = factory.CreatePublicClient();
        var statuses = new List<HttpStatusCode>();
        foreach (var id in ids)
        {
            using var response = await ReportAsync(client, id);
            statuses.Add(response.StatusCode);
        }

        Assert.Equal(Enumerable.Repeat(HttpStatusCode.NoContent, ReportEvent.MaxReportsPerHour), statuses.Take(ReportEvent.MaxReportsPerHour));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
    }

    [Fact]
    public async Task Out_check_confirms_only_scanned_safe_links_of_a_public_event()
    {
        var factory = CreateFactory();
        using var host = factory.CreateHostClient();
        var hostId = await host.SignInWithPhoneAsync(Phone(1));
        await using var db = Postgis.CreateDbContext();
        var venue = await SeedVenueAsync(db, Now(factory));
        var draft = await SeedDraftAsync(db, hostId, venue, Now(factory), joinInstructions: "Join https://bit.ly/abc or https://example.com/join");
        using (var publish = await host.PostEventActionAsync(draft.PublicId, "publish"))
        {
            Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        }

        using var client = factory.CreatePublicClient();
        var shortened = await CheckAsync(client, "https://bit.ly/abc", draft.PublicId);
        var unknown = await CheckAsync(client, "https://evil.example/x", draft.PublicId);
        var wrongEvent = await CheckAsync(client, "https://example.com/join", "bbbbbbbbbbbb");

        Assert.Equal(OutboundLinkStatus.Ok, shortened.GetProperty("status").GetString());
        Assert.Equal("https://bit.ly/abc", shortened.GetProperty("url").GetString());
        Assert.Equal("example.com", shortened.GetProperty("finalHost").GetString());
        Assert.Equal(OutboundLinkStatus.Unrecognized, unknown.GetProperty("status").GetString());
        Assert.Equal(OutboundLinkStatus.Unrecognized, wrongEvent.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Link_rescan_re_expands_shorteners_and_hides_events_that_now_point_at_malware()
    {
        var factory = CreateFactory(configureServices: SwitchableShortenerExpander.Register);
        using var host = factory.CreateHostClient();
        var hostId = await host.SignInWithPhoneAsync(Phone(1));
        await using var db = Postgis.CreateDbContext();
        var venue = await SeedVenueAsync(db, Now(factory));
        var draft = await SeedDraftAsync(db, hostId, venue, Now(factory), joinInstructions: "Sign up https://bit.ly/rink");
        using (var publish = await host.PostEventActionAsync(draft.PublicId, "publish"))
        {
            Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        }

        var job = factory.Services.GetRequiredService<LinkRescanJob>();
        factory.Clock.Advance(Duration.FromDays(1));
        var clean = await job.RunOnceAsync(Ct);
        Assert.Equal((JobRunStatus.Succeeded, 0L), (clean.Status, clean.Rows));
        Assert.Equal(EventStatus.Published, (await LoadAsync(db, draft.Id)).Status);

        factory.Services.GetRequiredService<SwitchableShortenerExpander>().PointsAtMalware = true;
        factory.Clock.Advance(Duration.FromDays(1));
        var result = await job.RunOnceAsync(Ct);

        Assert.Equal((JobRunStatus.Succeeded, 1L), (result.Status, result.Rows));
        var evt = await LoadAsync(db, draft.Id);
        Assert.Equal(EventStatus.Hidden, evt.Status);
        Assert.Equal(HiddenReason.LinkScan, evt.HiddenReason);
        Assert.True(await db.LinkScans.AnyAsync(scan => scan.EventId == draft.Id && scan.FinalHost == "malicious.test", Ct));
    }

    [Fact]
    public async Task Domain_sweep_hides_visible_events_linking_to_a_blocked_domain_or_subdomain()
    {
        var factory = CreateFactory();
        using var host = factory.CreateHostClient();
        var hostId = await host.SignInWithPhoneAsync(Phone(1));
        await using var db = Postgis.CreateDbContext();
        var venue = await SeedVenueAsync(db, Now(factory));
        var direct = await PublishAsync(host, await SeedDraftAsync(db, hostId, venue, Now(factory), joinInstructions: "Pay at https://pay.sketchy.com/x"));
        var clean = await PublishAsync(host, await SeedDraftAsync(db, hostId, venue, Now(factory)));
        db.Blocklist.Add(new BlocklistEntry
        {
            Kind = BlocklistKind.Domain,
            Value = "sketchy.com",
            Reason = "test",
            CreatedAt = Now(factory),
        });
        await db.SaveChangesAsync(Ct);

        var result = await factory.Services.GetRequiredService<DomainBlocklistSweepJob>().RunOnceAsync(Ct);

        Assert.Equal((JobRunStatus.Succeeded, 1L), (result.Status, result.Rows));
        var hidden = await LoadAsync(db, direct.Id);
        Assert.Equal(EventStatus.Hidden, hidden.Status);
        Assert.Equal(HiddenReason.DomainBlocklist, hidden.HiddenReason);
        Assert.Equal(EventStatus.Published, (await LoadAsync(db, clean.Id)).Status);
        Assert.Equal(0, (await factory.Services.GetRequiredService<DomainBlocklistSweepJob>().RunOnceAsync(Ct)).Rows);
    }

    [Fact]
    public async Task Twilio_usage_webhook_with_a_valid_signature_opens_the_sms_breaker()
    {
        var factory = TwilioFactory();
        using var client = factory.CreatePublicClient();
        var form = UsageForm();

        using var response = await PostWebhookAsync(client, form, TwilioSignatureValidator.Compute(TwilioToken, TwilioCallbackUrl, Pairs(form)));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var db = Postgis.CreateDbContext();
        var breaker = await db.Breakers.AsNoTracking().SingleAsync(row => row.Name == BreakerNames.OtpSms, Ct);
        Assert.Equal(new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.Zero), breaker.OpenUntil);
        Assert.Equal(BreakerOpener.TwilioWebhook, breaker.OpenedBy);
    }

    [Fact]
    public async Task Twilio_usage_webhook_rejects_bad_signatures_and_ignores_the_request_host()
    {
        var factory = TwilioFactory();
        using var client = factory.CreatePublicClient();
        var form = UsageForm();
        var signedForInternalHost = TwilioSignatureValidator.Compute(TwilioToken, "http://api:8080/v1/internal/twilio-usage", Pairs(form));
        var validSignature = TwilioSignatureValidator.Compute(TwilioToken, TwilioCallbackUrl, Pairs(form));

        using var forged = await PostWebhookAsync(client, form, "bm90LWEtc2lnbmF0dXJl");
        using var missing = await PostWebhookAsync(client, form, signature: null);
        using var internalHostSigned = await PostWebhookAsync(client, form, signedForInternalHost, host: "api:8080");
        using var tampered = await PostWebhookAsync(client, new Dictionary<string, string>(form) { ["CurrentValue"] = "1" }, validSignature);

        Assert.All([forged, missing, internalHostSigned, tampered], response => Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode));
        await using var db = Postgis.CreateDbContext();
        Assert.False(await db.Breakers.AnyAsync(row => row.Name == BreakerNames.OtpSms && row.OpenUntil != null, Ct));

        using var validFromInternalHost = await PostWebhookAsync(client, form, validSignature, host: "api:8080");
        Assert.Equal(HttpStatusCode.NoContent, validFromInternalHost.StatusCode);
    }

    private ApiFactory TwilioFactory() => CreateFactory(new Dictionary<string, string?>
    {
        ["Twilio:AuthToken"] = TwilioToken,
        ["Twilio:CallbackUrl"] = TwilioCallbackUrl,
    });

    private static Dictionary<string, string> UsageForm() => new(StringComparer.Ordinal)
    {
        ["AccountSid"] = "AC00000000000000000000000000000000",
        ["UsageTriggerSid"] = "UT00000000000000000000000000000000",
        ["CurrentValue"] = "51.20",
        ["TriggerValue"] = "50",
        ["UsageCategory"] = "sms",
    };

    private static IEnumerable<KeyValuePair<string, StringValues>> Pairs(Dictionary<string, string> form) =>
        form.Select(pair => new KeyValuePair<string, StringValues>(pair.Key, pair.Value));

    private static Task<HttpResponseMessage> PostWebhookAsync(HttpClient client, Dictionary<string, string> form, string? signature, string? host = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/internal/twilio-usage", UriKind.Relative))
        {
            Content = new FormUrlEncodedContent(form),
        };
        if (signature is not null)
        {
            request.Headers.Add(TwilioSignatureValidator.HeaderName, signature);
        }

        if (host is not null)
        {
            request.Headers.Host = host;
        }

        return client.SendAsync(request, Ct);
    }

    private async Task<(ApiFactory Factory, string PublicId, Guid EventId)> PublishedEventAsync()
    {
        var factory = CreateFactory();
        using var host = factory.CreateHostClient();
        var hostId = await host.SignInWithPhoneAsync(Phone(1));
        await using var db = Postgis.CreateDbContext();
        var venue = await SeedVenueAsync(db, Now(factory));
        var draft = await PublishAsync(host, await SeedDraftAsync(db, hostId, venue, Now(factory)));
        return (factory, draft.PublicId, draft.Id);
    }

    private static async Task<HockeyEvent> PublishAsync(HttpClient host, HockeyEvent draft)
    {
        using var publish = await host.PostEventActionAsync(draft.PublicId, "publish");
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        return draft;
    }

    private static Task<HttpResponseMessage> ReportAsync(
        HttpClient client, string publicId, string reason = "scam", string turnstileToken = PassingTurnstileToken) =>
        client.PostAsJsonAsync($"/v1/events/{publicId}/reports", new { reason, details = "Looks fake", turnstileToken }, Ct);

    private static async Task<System.Text.Json.JsonElement> CheckAsync(HttpClient client, string url, string eventId)
    {
        using var response = await client.GetAsync(new Uri($"/v1/out/check?u={Uri.EscapeDataString(url)}&e={eventId}", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        if (PublicId.IsValid(eventId))
        {
            Assert.Equal($"event-{eventId}", response.Headers.NonValidated["Cache-Tag"].ToString());
        }
        else
        {
            Assert.False(response.Headers.Contains("Cache-Tag"));
        }

        return await ReadJsonAsync(response);
    }

    private static void DisposeAll(IEnumerable<IDisposable> disposables)
    {
        foreach (var disposable in disposables)
        {
            disposable.Dispose();
        }
    }
}
