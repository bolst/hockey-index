using HockeyIndex.Api.Infrastructure.Http;

namespace HockeyIndex.Api.Tests.Unit;

public sealed class EdgeKeyAuthTests
{
    private static readonly EdgeKeyOptions Options = new() { Key = "current-key", PreviousKey = "previous-key" };

    [Theory]
    [InlineData("current-key")]
    [InlineData("previous-key")]
    public void Accepts_current_and_previous_keys(string presented) =>
        Assert.True(EdgeKeyAuth.IsValid(presented, Options));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("current-ke")]
    [InlineData("wrong")]
    public void Rejects_missing_or_wrong_keys(string? presented) =>
        Assert.False(EdgeKeyAuth.IsValid(presented, Options));

    [Fact]
    public void Rejects_everything_when_no_key_is_configured() =>
        Assert.False(EdgeKeyAuth.IsValid("", new EdgeKeyOptions()));
}
