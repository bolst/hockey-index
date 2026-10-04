using NodaTime;

namespace HockeyIndex.Api.Infrastructure.Time;

/// <param name="Instant">UTC instant, or null when the wall time does not exist (spring-forward gap).</param>
/// <param name="WasAmbiguous">The wall time occurs twice (fall-back overlap); the earlier instant was chosen.</param>
public sealed record VenueTimeResolution(DateTimeOffset? Instant, bool WasAmbiguous)
{
    public bool WasSkipped => Instant is null;
}

/// <summary>Converts venue wall times to UTC instants (AC-DS-6). Skipped times have no instant; ambiguous times map to the earlier one.</summary>
public sealed class VenueTimeConverter(IDateTimeZoneProvider zones)
{
    public bool IsKnownZone(string timeZone) => zones.GetZoneOrNull(timeZone) is not null;

    public VenueTimeResolution ToInstant(string timeZone, DateTime local)
    {
        var mapping = zones[timeZone].MapLocal(LocalDateTime.FromDateTime(local));
        return mapping.Count switch
        {
            0 => new VenueTimeResolution(null, WasAmbiguous: false),
            1 => new VenueTimeResolution(mapping.Single().ToDateTimeOffset().ToUniversalTime(), WasAmbiguous: false),
            _ => new VenueTimeResolution(mapping.First().ToDateTimeOffset().ToUniversalTime(), WasAmbiguous: true),
        };
    }

    /// <summary>The instant rendered with the venue's offset at that instant.</summary>
    public DateTimeOffset ToVenueOffset(string timeZone, DateTimeOffset instant) =>
        Instant.FromDateTimeOffset(instant).InZone(zones[timeZone]).ToDateTimeOffset();
}
