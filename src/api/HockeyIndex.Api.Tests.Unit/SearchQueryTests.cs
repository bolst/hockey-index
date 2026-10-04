using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Features.Discovery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using NodaTime;

namespace HockeyIndex.Api.Tests.Unit;

public sealed class SearchQueryTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 10, 3, 12, 34);
    private const string Valid = "lat=43.66&lng=-79.38&r=25&from=2026-10-03T12:00:00Z&days=14";

    private static SearchQuery? Parse(string queryString, out IReadOnlyDictionary<string, string> errors) =>
        SearchQuery.TryParse(new QueryCollection(QueryHelpers.ParseQuery(queryString)), Now, out errors);

    [Fact]
    public void Parses_a_canonical_query_with_defaults()
    {
        var query = Parse(Valid, out var errors);

        Assert.Empty(errors);
        Assert.NotNull(query);
        Assert.Equal(43.66m, query.Latitude);
        Assert.Equal(-79.38m, query.Longitude);
        Assert.Equal(25, query.RadiusMiles);
        Assert.Equal(Instant.FromUtc(2026, 10, 3, 12, 0), query.From);
        Assert.Equal(Instant.FromUtc(2026, 10, 17, 12, 0), query.To);
        Assert.Equal(0, query.SkillMin);
        Assert.Equal(7, query.SkillMax);
        Assert.Null(query.Type);
        Assert.Null(query.MaxFeeCents);
        Assert.Equal(25 * 1609.344, query.RadiusMeters, 6);
    }

    [Fact]
    public void Parses_optional_filters()
    {
        var query = Parse(Valid + "&smin=3&smax=5&type=league&maxFee=0", out _);

        Assert.NotNull(query);
        Assert.Equal(3, query.SkillMin);
        Assert.Equal(5, query.SkillMax);
        Assert.Equal(EventType.League, query.Type);
        Assert.Equal(0, query.MaxFeeCents);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    [InlineData(1500)]
    [InlineData(2000)]
    [InlineData(2500)]
    [InlineData(3000)]
    [InlineData(5000)]
    public void Accepts_every_fee_bucket(int maxFee) =>
        Assert.Equal(maxFee, Parse($"{Valid}&maxFee={maxFee}", out _)?.MaxFeeCents);

    [Theory]
    [InlineData("lat=43.661&lng=-79.38&r=25&from=2026-10-03T12:00:00Z&days=14", "lat")]
    [InlineData("lat=43.66&lng=-79.3801&r=25&from=2026-10-03T12:00:00Z&days=14", "lng")]
    [InlineData("lat=91&lng=-79.38&r=25&from=2026-10-03T12:00:00Z&days=14", "lat")]
    [InlineData("lat=43.66&lng=181&r=25&from=2026-10-03T12:00:00Z&days=14", "lng")]
    [InlineData("lat=4e1&lng=-79.38&r=25&from=2026-10-03T12:00:00Z&days=14", "lat")]
    [InlineData("lat=043.66&lng=-79.38&r=25&from=2026-10-03T12:00:00Z&days=14", "lat")]
    [InlineData("lat=43.66&lng=-79.38&r=20&from=2026-10-03T12:00:00Z&days=14", "r")]
    [InlineData("lat=43.66&lng=-79.38&r=025&from=2026-10-03T12:00:00Z&days=14", "r")]
    [InlineData("lat=43.66&lng=-79.38&r=25&from=2026-10-03T12:30:00Z&days=14", "from")]
    [InlineData("lat=43.66&lng=-79.38&r=25&from=2026-10-03T12:00:00.000Z&days=14", "from")]
    [InlineData("lat=43.66&lng=-79.38&r=25&from=2026-10-03T08:00:00-04:00&days=14", "from")]
    [InlineData("lat=43.66&lng=-79.38&r=25&from=2026-09-30T12:00:00Z&days=14", "from")]
    [InlineData("lat=43.66&lng=-79.38&r=25&from=2028-10-03T12:00:00Z&days=14", "from")]
    [InlineData("lat=43.66&lng=-79.38&r=25&from=2026-10-03T12:00:00Z&days=10", "days")]
    [InlineData("lat=43.66&lng=-79.38&r=25&from=2026-10-03T12:00:00Z", "days")]
    [InlineData(Valid + "&smin=8", "smin")]
    [InlineData(Valid + "&smax=-1", "smax")]
    [InlineData(Valid + "&smin=5&smax=3", "smin")]
    [InlineData(Valid + "&type=pickup", "type")]
    [InlineData(Valid + "&maxFee=1200", "maxFee")]
    [InlineData(Valid + "&maxFee=", "maxFee")]
    [InlineData(Valid + "&page=2", "page")]
    [InlineData(Valid + "&r=50", "r")]
    public void Rejects_non_canonical_input(string queryString, string field)
    {
        var query = Parse(queryString, out var errors);

        Assert.Null(query);
        Assert.Contains(field, errors.Keys);
    }
}
