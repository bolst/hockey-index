using HockeyIndex.Api.Infrastructure.Time;
using NodaTime;

namespace HockeyIndex.Api.Tests.Unit;

public sealed class VenueTimeConverterTests
{
    private const string Toronto = "America/Toronto";
    private readonly VenueTimeConverter converter = new(DateTimeZoneProviders.Tzdb);

    [Fact]
    public void Spring_forward_gap_has_no_instant()
    {
        var resolution = converter.ToInstant(Toronto, new DateTime(2027, 3, 14, 2, 30, 0));

        Assert.True(resolution.WasSkipped);
        Assert.False(resolution.WasAmbiguous);
    }

    [Fact]
    public void Fall_back_overlap_resolves_to_the_earlier_instant()
    {
        var resolution = converter.ToInstant(Toronto, new DateTime(2026, 11, 1, 1, 30, 0));

        Assert.True(resolution.WasAmbiguous);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 5, 30, 0, TimeSpan.Zero), resolution.Instant);
        Assert.Equal(TimeSpan.Zero, resolution.Instant!.Value.Offset);
    }

    [Theory]
    [InlineData("America/Regina", 6)]
    [InlineData("America/Phoenix", 7)]
    public void Zones_without_daylight_saving_keep_one_offset_all_year(string timeZone, int hoursBehindUtc)
    {
        var winter = converter.ToInstant(timeZone, new DateTime(2027, 1, 15, 12, 0, 0));
        var summer = converter.ToInstant(timeZone, new DateTime(2027, 7, 15, 12, 0, 0));

        Assert.Equal(new DateTimeOffset(2027, 1, 15, 12 + hoursBehindUtc, 0, 0, TimeSpan.Zero), winter.Instant);
        Assert.Equal(new DateTimeOffset(2027, 7, 15, 12 + hoursBehindUtc, 0, 0, TimeSpan.Zero), summer.Instant);
    }

    [Fact]
    public void League_end_is_midnight_after_the_last_day_in_venue_time()
    {
        var lastDay = new DateOnly(2027, 3, 31);

        var end = converter.ToInstant(Toronto, lastDay.AddDays(1).ToDateTime(TimeOnly.MinValue));

        Assert.Equal(new DateTimeOffset(2027, 4, 1, 4, 0, 0, TimeSpan.Zero), end.Instant);
    }

    [Fact]
    public void Same_wall_time_a_week_later_across_fall_back_shifts_utc_by_an_hour()
    {
        var before = converter.ToInstant(Toronto, new DateTime(2026, 10, 28, 19, 0, 0));
        var after = converter.ToInstant(Toronto, new DateTime(2026, 11, 4, 19, 0, 0));

        Assert.Equal(TimeSpan.FromDays(7) + TimeSpan.FromHours(1), after.Instant - before.Instant);
    }

    [Fact]
    public void Venue_offset_reflects_daylight_saving_at_the_instant()
    {
        var summer = converter.ToVenueOffset(Toronto, new DateTimeOffset(2027, 7, 1, 23, 0, 0, TimeSpan.Zero));
        var winter = converter.ToVenueOffset(Toronto, new DateTimeOffset(2027, 1, 1, 23, 0, 0, TimeSpan.Zero));

        Assert.Equal(TimeSpan.FromHours(-4), summer.Offset);
        Assert.Equal(19, summer.Hour);
        Assert.Equal(TimeSpan.FromHours(-5), winter.Offset);
        Assert.Equal(18, winter.Hour);
    }

    [Theory]
    [InlineData(Toronto, true)]
    [InlineData("Mars/Olympus_Mons", false)]
    public void Recognises_tzdb_zones(string timeZone, bool expected) => Assert.Equal(expected, converter.IsKnownZone(timeZone));
}
