using HockeyIndex.Api.Infrastructure.Text;

namespace HockeyIndex.Api.Tests.Unit;

public sealed class TextNormalizerTests
{
    [Theory]
    [InlineData("Centre Vidéotron", "centre videotron")]
    [InlineData("  Scotiabank\t\tArena \n ", "scotiabank arena")]
    [InlineData("ÉCOLE  Sainte-Thérèse", "ecole sainte-therese")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Normalizes_case_accents_and_whitespace(string? input, string expected) =>
        Assert.Equal(expected, TextNormalizer.Normalize(input));
}
