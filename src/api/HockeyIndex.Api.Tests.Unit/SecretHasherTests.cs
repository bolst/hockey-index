using HockeyIndex.Api.Infrastructure.Auth;
using Microsoft.Extensions.Options;

namespace HockeyIndex.Api.Tests.Unit;

public sealed class SecretHasherTests
{
    private static SecretHasher Hasher(string key = "unit-test-hmac-key-0123456789abcdef") =>
        new(Options.Create(new HmacOptions { Key = key }));

    [Fact]
    public void Hash_is_stable_for_the_same_key_and_value()
    {
        Assert.Equal(Hasher().HashPhone("+12064123456"), Hasher().HashPhone("+12064123456"));
    }

    [Fact]
    public void Purposes_and_keys_separate_hashes()
    {
        var hasher = Hasher();

        Assert.NotEqual(hasher.HashPhone("x"), hasher.HashEmail("x"));
        Assert.NotEqual(hasher.HashPhone("x"), Hasher("another-unit-test-hmac-key-0123456789").HashPhone("x"));
    }
}
