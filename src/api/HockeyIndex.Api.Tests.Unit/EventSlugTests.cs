using HockeyIndex.Api.Features.Discovery;

namespace HockeyIndex.Api.Tests.Unit;

/// <summary>Same fixtures as <c>src/web/src/shared-render/event-summary.spec.ts</c>; the two slug functions MUST agree.</summary>
public sealed class EventSlugTests
{
    [Theory]
    [InlineData("Sunday Night Skate", "sunday-night-skate")]
    [InlineData("  Beer League -- Tier 2!  ", "beer-league-tier-2")]
    [InlineData("Équipe Montréal Hockey", "equipe-montreal-hockey")]
    [InlineData("Ünïcödé Ïcé", "unicode-ice")]
    [InlineData("日本語", "")]
    public void Slugs_titles(string title, string slug) => Assert.Equal(slug, EventSlug.From(title));

    [Fact]
    public void Truncates_to_sixty_characters_without_a_trailing_hyphen()
    {
        Assert.Equal(new string('a', 59), EventSlug.From(new string('a', 59) + " b"));
        Assert.Equal(new string('x', 60), EventSlug.From(new string('x', 70)));
    }

    [Fact]
    public void Omits_an_empty_slug_from_the_canonical_path()
    {
        Assert.Equal("/e/abcdefgh23", EventSlug.CanonicalPath("abcdefgh23", "日本語"));
        Assert.Equal("/e/abcdefgh23/skate", EventSlug.CanonicalPath("abcdefgh23", "Skate"));
    }
}
