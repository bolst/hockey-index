namespace HockeyIndex.Api.Features.Venues;

public sealed class VenueOptions
{
    public const string SectionName = "Venues";

    /// <summary>How long the <c>mapbox</c> breaker stays open after a provider fault.</summary>
    public TimeSpan BreakerOpenFor { get; set; } = TimeSpan.FromMinutes(5);
}
