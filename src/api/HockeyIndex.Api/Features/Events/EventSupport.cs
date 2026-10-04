using System.Globalization;
using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Features.Safety;
using HockeyIndex.Api.Infrastructure.Persistence;
using HockeyIndex.Api.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace HockeyIndex.Api.Features.Events;

public static class EventRows
{
    /// <summary>Untracked snapshot of a host's own event.</summary>
    public static Task<HockeyEvent?> FindOwnedAsync(AppDbContext db, string publicId, Guid hostId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        return PublicId.IsValid(publicId)
            ? db.Events.AsNoTracking().SingleOrDefaultAsync(evt => evt.PublicId == publicId && evt.HostId == hostId, cancellationToken)
            : Task.FromResult<HockeyEvent?>(null);
    }

    /// <summary>Tracked, fresh row under <c>SELECT … FOR UPDATE</c>. MUST run inside a transaction.</summary>
    public static async Task<HockeyEvent?> LockAsync(AppDbContext db, Guid eventId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Event row locks MUST be taken inside a transaction.");
        }

        var rows = await db.Events
            .FromSql($"SELECT e.*, e.xmin FROM events e WHERE e.id = {eventId} FOR UPDATE")
            .ToListAsync(cancellationToken);
        return rows.SingleOrDefault();
    }
}

public static class EventETags
{
    public static string Format(uint version) => $"\"{version.ToString(CultureInfo.InvariantCulture)}\"";

    /// <returns>False when <c>If-Match</c> is missing; <paramref name="version"/> is null when it is present but unparsable.</returns>
    public static bool TryReadIfMatch(HttpRequest request, out uint? version)
    {
        ArgumentNullException.ThrowIfNull(request);
        version = null;
        var header = request.Headers.IfMatch.ToString().Trim();
        if (header.Length == 0)
        {
            return false;
        }

        if (header.StartsWith("W/", StringComparison.Ordinal))
        {
            header = header[2..];
        }

        if (uint.TryParse(header.Trim('"'), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            version = parsed;
        }

        return true;
    }

    public static void Write(HttpResponse response, uint version)
    {
        ArgumentNullException.ThrowIfNull(response);
        response.Headers[HeaderNames.ETag] = Format(version);
    }
}

/// <summary>Builds responses with the venue summary and writes the <c>ETag</c>.</summary>
public sealed class EventResponder(AppDbContext db, VenueTimeConverter times)
{
    public async Task<EventResponse> ToResponseAsync(
        HockeyEvent evt, HttpResponse? response, CancellationToken cancellationToken, IReadOnlyList<string>? resolvedAmbiguousTimes = null)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var venue = await db.Venues.AsNoTracking().SingleAsync(candidate => candidate.Id == evt.VenueId, cancellationToken);
        if (response is not null)
        {
            EventETags.Write(response, evt.Version);
        }

        return EventResponse.From(evt, venue, times, resolvedAmbiguousTimes);
    }
}

/// <summary>Stores one <c>link_scans</c> row per scanned URL. Runs outside any transaction, right after the scan.</summary>
public sealed class LinkScanRecorder(AppDbContext db)
{
    public async Task RecordAsync(Guid eventId, LinkScanResult result, DateTimeOffset checkedAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Links.Count == 0)
        {
            return;
        }

        var scans = result.Links.Select(link => new LinkScan
        {
            EventId = eventId,
            Url = Truncate(link.Url.AbsoluteUri, 2048),
            FinalUrl = Truncate(link.FinalUrl.AbsoluteUri, 2048),
            FinalHost = Truncate(link.FinalUrl.IdnHost, 255),
            RedirectHops = link.RedirectHops,
            Verdict = link.Verdict switch
            {
                LinkVerdict.Safe => "safe",
                LinkVerdict.Malicious => "malicious",
                _ => "unknown",
            },
            Provider = link.Source switch
            {
                LinkVerdictSource.WebRisk => "webrisk",
                LinkVerdictSource.Blocklist => "blocklist",
                _ => "none",
            },
            ThreatTypes = [.. link.ThreatTypes],
            CheckedAt = checkedAt,
        }).ToList();

        db.LinkScans.AddRange(scans);
        await db.SaveChangesAsync(cancellationToken);
        foreach (var scan in scans)
        {
            db.Entry(scan).State = EntityState.Detached;
        }
    }

    private static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];
}
