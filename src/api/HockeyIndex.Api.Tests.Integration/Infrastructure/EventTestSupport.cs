using System.Net.Http.Headers;
using System.Net.Http.Json;
using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Features.Safety;
using HockeyIndex.Api.Features.Venues;
using HockeyIndex.Api.Infrastructure.Persistence;
using HockeyIndex.Api.Infrastructure.Text;
using HockeyIndex.Api.Infrastructure.Time;
using HockeyIndex.Api.Integrations.Fakes;
using HockeyIndex.Api.Integrations.WebRisk;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NodaTime;
using NpgsqlTypes;

namespace HockeyIndex.Api.Tests.Integration.Infrastructure;

/// <summary>
/// Wraps the fake reputation provider. <see cref="IsDown"/> makes every lookup a provider fault (opens the breaker);
/// URLs on <see cref="GatedHost"/> block until <see cref="Release"/> so a test can act while a scan is in flight.
/// </summary>
public sealed class ControllableReputationProvider : IUrlReputationProvider
{
    public const string GatedHost = "gate.test";

    private readonly FakeUrlReputationProvider inner = new();
    private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsDown { get; set; }

    public Task Entered => entered.Task;

    public void Release() => released.TrySetResult();

    public async Task<UrlReputation> LookupAsync(Uri url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (IsDown)
        {
            return UrlReputation.Failed(isProviderFault: true);
        }

        if (url.Host == GatedHost)
        {
            entered.TrySetResult();
            await released.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            return UrlReputation.Safe();
        }

        return await inner.LookupAsync(url, cancellationToken);
    }

    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<ControllableReputationProvider>();
        services.AddSingleton<IUrlReputationProvider>(provider => provider.GetRequiredService<ControllableReputationProvider>());
    }
}

public static class EventTestSupport
{
    public const string Toronto = "America/Toronto";
    public const string CleanJoin = "Sign up at https://example.com/join";
    public const string MaliciousUrl = "https://malicious.test/phish";
    public const string UnavailableUrl = "https://unavailable.test/join";

    private static readonly VenueTimeConverter Times = new(DateTimeZoneProviders.Tzdb);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<Venue> SeedVenueAsync(AppDbContext db, DateTimeOffset now, string timeZone = Toronto, string country = "CA")
    {
        ArgumentNullException.ThrowIfNull(db);
        var venue = new Venue
        {
            PublicId = PublicId.New(),
            Name = "Test Arena",
            NameNormalized = TextNormalizer.Normalize("Test Arena"),
            AddressLine = "1 Test Street",
            City = "Toronto",
            Region = "ON",
            Country = country,
            Geo = VenueGeometry.Point(43.66, -79.38),
            TimeZone = timeZone,
            Provider = VenueProviders.Manual,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Venues.Add(venue);
        await db.SaveChangesAsync(Ct);
        return venue;
    }

    /// <summary>Inserts a scrimmage draft directly (bypasses the HostWrite rate limit).</summary>
    public static async Task<HockeyEvent> SeedDraftAsync(
        AppDbContext db, Guid hostId, Venue venue, DateTimeOffset now, DateTime? startsLocal = null, string joinInstructions = CleanJoin)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(venue);
        var start = startsLocal ?? new DateTime(2026, 10, 10, 19, 0, 0);
        var end = start.AddMinutes(90);
        var evt = new HockeyEvent
        {
            PublicId = PublicId.New(),
            HostId = hostId,
            VenueId = venue.Id,
            Type = EventType.Scrimmage,
            Title = "Friday scrimmage",
            StartsLocal = start,
            EndsLocal = end,
            StartsAt = Times.ToInstant(venue.TimeZone, start).Instant!.Value,
            EndsAt = Times.ToInstant(venue.TimeZone, end).Instant!.Value,
            SkillRange = new NpgsqlRange<int>(2, true, 4, true),
            FeeCents = 1500,
            Currency = "CAD",
            JoinInstructions = joinInstructions,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Events.Add(evt);
        await db.SaveChangesAsync(Ct);
        return evt;
    }

    /// <summary>Test-only shortcut that forces a lifecycle state in SQL, as an admin action or older data would leave it.</summary>
    public static Task ForceStatusAsync(
        AppDbContext db, Guid eventId, string status, string? hiddenReason = null, string? hiddenFromStatus = null, DateTimeOffset? archivedAt = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Database.ExecuteSqlAsync(
            $"""
            UPDATE events SET status = {status}, hidden_reason = {hiddenReason}, hidden_from_status = {hiddenFromStatus},
                published_at = COALESCE(published_at, created_at), archived_at = {archivedAt}
            WHERE id = {eventId}
            """,
            Ct);
    }

    public static Task<HockeyEvent> LoadAsync(AppDbContext db, Guid eventId)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Events.AsNoTracking().SingleAsync(candidate => candidate.Id == eventId, Ct);
    }

    public static object ScrimmageBody(
        string venueId,
        string joinInstructions = CleanJoin,
        string startsLocal = "2026-10-10T19:00:00",
        string endsLocal = "2026-10-10T20:30:00",
        string title = "Friday scrimmage",
        object? skill = null,
        int? feeCents = 1500) => new
        {
            venueId,
            type = "scrimmage",
            title,
            rinkLabel = "Rink 2",
            startsLocal,
            endsLocal,
            skill = skill ?? new { min = 2, max = 4 },
            feeCents,
            joinInstructions,
        };

    public static Task<HttpResponseMessage> PutWithETagAsync(this HttpClient client, string publicId, object body, string? etag)
    {
        ArgumentNullException.ThrowIfNull(client);
        var request = new HttpRequestMessage(HttpMethod.Put, new Uri($"/v1/host/events/{publicId}", UriKind.Relative))
        {
            Content = JsonContent.Create(body),
        };
        if (etag is not null)
        {
            request.Headers.IfMatch.Add(new EntityTagHeaderValue(etag));
        }

        return client.SendAsync(request, Ct);
    }

    public static Task<HttpResponseMessage> PostEventActionAsync(this HttpClient client, string publicId, string action)
    {
        ArgumentNullException.ThrowIfNull(client);
        return client.PostAsync(new Uri($"/v1/host/events/{publicId}/{action}", UriKind.Relative), content: null, Ct);
    }

    public static async Task WaitUntilAsync(Func<bool> condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(20, timeout.Token);
        }
    }

    /// <summary>Clears provider-fault state: the scanner answers again and the breaker window has passed.</summary>
    public static void RecoverScanner(ApiFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        factory.Services.GetRequiredService<ControllableReputationProvider>().IsDown = false;
        var openFor = factory.Services.GetRequiredService<IOptions<LinkScanningOptions>>().Value.BreakerOpenFor;
        factory.Clock.Advance(Duration.FromTimeSpan(openFor) + Duration.FromMinutes(1));
    }
}
