using System.Net;
using System.Text;
using HockeyIndex.Api.Infrastructure.Http;

namespace HockeyIndex.Api.Tests.Unit;

public sealed class SsrfSafeHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("10.1.2.3")]
    [InlineData("100.64.0.1")]
    [InlineData("100.127.255.254")]
    [InlineData("127.0.0.1")]
    [InlineData("127.255.255.255")]
    [InlineData("169.254.169.254")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.0.0.8")]
    [InlineData("192.0.2.1")]
    [InlineData("192.168.1.1")]
    [InlineData("198.18.0.1")]
    [InlineData("198.51.100.7")]
    [InlineData("203.0.113.9")]
    [InlineData("224.0.0.251")]
    [InlineData("239.255.255.250")]
    [InlineData("240.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("::ffff:8.8.8.8")]
    [InlineData("::127.0.0.1")]
    [InlineData("64:ff9b::a00:1")]
    [InlineData("2001:db8::1")]
    [InlineData("2001::1")]
    [InlineData("2002:a00:1::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("fe80::1")]
    [InlineData("fec0::1")]
    [InlineData("ff02::1")]
    public void Non_public_addresses_are_refused(string address) =>
        Assert.False(SsrfSafeHandler.IsPublicAddress(IPAddress.Parse(address)));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("93.184.216.34")]
    [InlineData("172.32.0.1")]
    [InlineData("100.128.0.1")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("2a00:1450:4001:80b::200e")]
    public void Public_addresses_are_allowed(string address) =>
        Assert.True(SsrfSafeHandler.IsPublicAddress(IPAddress.Parse(address)));

    [Theory]
    [InlineData("2130706433")]
    [InlineData("0177.0.0.1")]
    [InlineData("0x7f.0.0.1")]
    [InlineData("127.1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("[::1]")]
    public async Task Ip_literal_hosts_in_any_notation_never_connect(string host)
    {
        var probe = new ConnectProbe(_ => [IPAddress.Parse("93.184.216.34")]);

        await Assert.ThrowsAsync<SsrfBlockedException>(() => probe.Handler.ConnectAsync(new DnsEndPoint(host, 80), Ct).AsTask());

        Assert.Empty(probe.Connected);
    }

    [Theory]
    [InlineData("10.0.0.5")]
    [InlineData("169.254.169.254")]
    [InlineData("::ffff:192.168.0.1")]
    [InlineData("fd00::5")]
    public async Task Hosts_resolving_to_private_addresses_never_connect(string resolved)
    {
        var probe = new ConnectProbe(_ => [IPAddress.Parse(resolved)]);

        await Assert.ThrowsAsync<SsrfBlockedException>(() => probe.Handler.ConnectAsync(new DnsEndPoint("evil.example", 443), Ct).AsTask());

        Assert.Empty(probe.Connected);
    }

    [Fact]
    public async Task Any_private_address_in_the_answer_refuses_the_host()
    {
        var probe = new ConnectProbe(_ => [IPAddress.Parse("93.184.216.34"), IPAddress.Parse("127.0.0.1")]);

        await Assert.ThrowsAsync<SsrfBlockedException>(() => probe.Handler.ConnectAsync(new DnsEndPoint("mixed.example", 443), Ct).AsTask());

        Assert.Empty(probe.Connected);
    }

    [Fact]
    public async Task Empty_dns_answer_is_refused()
    {
        var probe = new ConnectProbe(_ => []);

        await Assert.ThrowsAsync<SsrfBlockedException>(() => probe.Handler.ConnectAsync(new DnsEndPoint("nothing.example", 443), Ct).AsTask());
    }

    [Theory]
    [InlineData(22)]
    [InlineData(8080)]
    [InlineData(6379)]
    public async Task Ports_other_than_80_and_443_are_refused_before_dns(int port)
    {
        var probe = new ConnectProbe(_ => [IPAddress.Parse("93.184.216.34")]);

        await Assert.ThrowsAsync<SsrfBlockedException>(() => probe.Handler.ConnectAsync(new DnsEndPoint("example.com", port), Ct).AsTask());

        Assert.Equal(0, probe.Resolutions);
        Assert.Empty(probe.Connected);
    }

    [Fact]
    public async Task Connects_to_the_vetted_address_and_port()
    {
        var probe = new ConnectProbe(_ => [IPAddress.Parse("93.184.216.34")]);

        await using var stream = await probe.Handler.ConnectAsync(new DnsEndPoint("example.com", 443), Ct);

        Assert.Equal(new IPEndPoint(IPAddress.Parse("93.184.216.34"), 443), Assert.Single(probe.Connected));
    }

    [Fact]
    public async Task Dns_rebinding_cannot_redirect_a_connection_through_HttpClient()
    {
        var answers = new Queue<IPAddress[]>([[IPAddress.Parse("93.184.216.34")], [IPAddress.Parse("127.0.0.1")]]);
        var probe = new ConnectProbe(_ => answers.Dequeue());
        using var client = new HttpClient(probe.Handler.CreateHttpHandler());

        using var first = await client.SendAsync(CloseAfter("http://rebind.example/"), Ct);
        var second = await Assert.ThrowsAnyAsync<HttpRequestException>(() => client.SendAsync(CloseAfter("http://rebind.example/"), Ct));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.IsType<SsrfBlockedException>(second.InnerException ?? second, exactMatch: false);
        Assert.Equal(new IPEndPoint(IPAddress.Parse("93.184.216.34"), 80), Assert.Single(probe.Connected));
        Assert.Equal(2, probe.Resolutions);
    }

    [Fact]
    public async Task HttpClient_never_connects_to_decimal_loopback_or_a_non_default_port()
    {
        var probe = new ConnectProbe(_ => [IPAddress.Parse("93.184.216.34")]);
        using var client = new HttpClient(probe.Handler.CreateHttpHandler());

        await Assert.ThrowsAnyAsync<HttpRequestException>(() => client.GetAsync(new Uri("http://2130706433/"), Ct));
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => client.GetAsync(new Uri("http://example.com:8080/"), Ct));

        Assert.Empty(probe.Connected);
    }

    [Fact]
    public void Handler_disables_redirects_and_proxies()
    {
        using var handler = new SsrfSafeHandler().CreateHttpHandler();

        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseProxy);
        Assert.NotNull(handler.ConnectCallback);
    }

    private static HttpRequestMessage CloseAfter(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.ConnectionClose = true;
        return request;
    }

    private sealed class ConnectProbe
    {
        public ConnectProbe(Func<string, IPAddress[]> resolve)
        {
            Handler = new SsrfSafeHandler(
                (host, _) =>
                {
                    Resolutions++;
                    return Task.FromResult(resolve(host));
                },
                (endpoint, _) =>
                {
                    Connected.Add(endpoint);
                    return ValueTask.FromResult<Stream>(new CannedHttpStream());
                });
        }

        public SsrfSafeHandler Handler { get; }

        public List<IPEndPoint> Connected { get; } = [];

        public int Resolutions { get; private set; }
    }

    /// <summary>Accepts any request bytes and answers with one empty 200 response.</summary>
    private sealed class CannedHttpStream : Stream
    {
        private readonly MemoryStream response = new(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => response.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            response.ReadAsync(buffer, cancellationToken);

        public override void Write(byte[] buffer, int offset, int count)
        {
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                response.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
