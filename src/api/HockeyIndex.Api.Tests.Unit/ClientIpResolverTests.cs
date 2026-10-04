using System.Net;
using HockeyIndex.Api.Infrastructure.Http;

namespace HockeyIndex.Api.Tests.Unit;

public sealed class ClientIpResolverTests
{
    [Fact]
    public void Ipv4_address_is_its_own_key()
    {
        Assert.True(ClientIpResolver.TryResolve("203.0.113.7", out var clientIp));

        Assert.Equal(IPAddress.Parse("203.0.113.7"), clientIp.Address);
        Assert.Equal(new byte[] { 203, 0, 113, 7 }, clientIp.Key.ToArray());
    }

    [Fact]
    public void Ipv6_addresses_in_the_same_64_share_a_key()
    {
        Assert.True(ClientIpResolver.TryResolve("2001:db8:aaaa:bbbb:1:2:3:4", out var first));
        Assert.True(ClientIpResolver.TryResolve("2001:db8:aaaa:bbbb:ffff:ffff:ffff:ffff", out var second));

        Assert.Equal(first.KeyHex, second.KeyHex);
        Assert.Equal(8, first.Key.Length);
        Assert.Equal(IPAddress.Parse("2001:db8:aaaa:bbbb::"), first.Address);
    }

    [Fact]
    public void Ipv6_addresses_in_different_64s_have_different_keys()
    {
        Assert.True(ClientIpResolver.TryResolve("2001:db8:aaaa:bbbb::1", out var first));
        Assert.True(ClientIpResolver.TryResolve("2001:db8:aaaa:bbbc::1", out var second));

        Assert.NotEqual(first.KeyHex, second.KeyHex);
    }

    [Fact]
    public void Ipv4_mapped_ipv6_is_treated_as_ipv4()
    {
        Assert.True(ClientIpResolver.TryResolve("::ffff:198.51.100.20", out var clientIp));

        Assert.Equal(IPAddress.Parse("198.51.100.20"), clientIp.Address);
        Assert.Equal(4, clientIp.Key.Length);
    }

    [Fact]
    public void Surrounding_whitespace_is_ignored()
    {
        Assert.True(ClientIpResolver.TryResolve("  192.0.2.1 ", out var clientIp));

        Assert.Equal(IPAddress.Parse("192.0.2.1"), clientIp.Address);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-ip")]
    [InlineData("203.0.113.7, 10.0.0.1")]
    public void Missing_or_invalid_header_is_rejected(string? headerValue)
    {
        Assert.False(ClientIpResolver.TryResolve(headerValue, out var clientIp));
        Assert.Null(clientIp);
    }
}
