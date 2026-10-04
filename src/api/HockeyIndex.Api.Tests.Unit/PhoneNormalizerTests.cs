using HockeyIndex.Api.Infrastructure.Auth;

namespace HockeyIndex.Api.Tests.Unit;

public sealed class PhoneNormalizerTests
{
    [Theory]
    [InlineData("(206) 412-3456", "+12064123456", "206412")]
    [InlineData("+1 206 412 3456", "+12064123456", "206412")]
    [InlineData("206.412.3456", "+12064123456", "206412")]
    [InlineData("+1 416 555 0199", "+14165550199", "416555")]
    [InlineData("+1 604 412 1000", "+16044121000", "604412")]
    public void Us_and_canadian_numbers_normalize_to_e164(string input, string e164, string npaNxx)
    {
        var result = PhoneNormalizer.Normalize(input);

        Assert.True(result.IsValid);
        Assert.Equal(e164, result.Phone!.E164);
        Assert.Equal(npaNxx, result.Phone.NpaNxx);
    }

    [Theory]
    [InlineData("+1 876 555 0100")]
    [InlineData("+1 787 555 0100")]
    [InlineData("+1 284 496 1234")]
    [InlineData("+44 20 7946 0958")]
    [InlineData("+52 55 1234 5678")]
    public void Other_regions_are_rejected(string input)
    {
        Assert.Equal(PhoneRejection.Region, PhoneNormalizer.Normalize(input).Rejection);
    }

    [Theory]
    [InlineData("+1 800 234 5678")]
    [InlineData("+1 888 234 5678")]
    public void Toll_free_numbers_are_rejected(string input)
    {
        Assert.Equal(PhoneRejection.TollFree, PhoneNormalizer.Normalize(input).Rejection);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("+1 206 412")]
    [InlineData("+1 123 456 7890")]
    public void Garbage_is_invalid(string? input)
    {
        var result = PhoneNormalizer.Normalize(input);

        Assert.Equal(PhoneRejection.Invalid, result.Rejection);
        Assert.Null(result.Phone);
    }
}
