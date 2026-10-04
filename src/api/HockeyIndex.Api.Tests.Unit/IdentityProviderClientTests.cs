using System.Net;
using System.Text;
using HockeyIndex.Api.Integrations.Email;
using HockeyIndex.Api.Integrations.Fakes;
using HockeyIndex.Api.Integrations.Turnstile;
using HockeyIndex.Api.Integrations.Twilio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace HockeyIndex.Api.Tests.Unit;

public sealed class IdentityProviderClientTests : IDisposable
{
    private const string Phone = "+12064123456";
    private readonly WireMockServer server = WireMockServer.Start();
    private readonly HttpClient http = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Uri BaseUrl => new(server.Url! + "/");

    [Fact]
    public async Task Verify_start_posts_sms_channel_with_basic_auth()
    {
        Respond("/v2/Services/VA123/Verifications", 201, """{"status":"pending"}""");

        Assert.True(await VerifyClient().StartAsync(Phone, Ct));

        var request = Assert.Single(server.LogEntries).RequestMessage!;
        Assert.Equal("POST", request.Method);
        Assert.Contains("To=%2B12064123456", request.Body, StringComparison.Ordinal);
        Assert.Contains("Channel=sms", request.Body, StringComparison.Ordinal);
        var expectedAuth = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("AC123:secret"));
        Assert.Equal(expectedAuth, request.Headers!["Authorization"].Single());
    }

    [Theory]
    [InlineData(200, """{"status":"approved"}""", true)]
    [InlineData(200, """{"status":"pending"}""", false)]
    [InlineData(404, """{"code":20404}""", false)]
    public async Task Verify_check_approves_only_approved_status(int status, string body, bool expected)
    {
        Respond("/v2/Services/VA123/VerificationCheck", status, body);

        Assert.Equal(expected, await VerifyClient().CheckAsync(Phone, "123456", Ct));
    }

    [Theory]
    [InlineData("mobile")]
    [InlineData("landline")]
    [InlineData("nonFixedVoip")]
    public async Task Lookup_reads_line_type_intelligence(string lineType)
    {
        server.Given(Request.Create().WithPath(new WireMock.Matchers.WildcardMatcher("/v2/PhoneNumbers/*12064123456")).WithParam("Fields", "line_type_intelligence").UsingGet())
            .RespondWith(Json(200, $$$"""{"line_type_intelligence":{"type":"{{{lineType}}}"}}"""));

        Assert.Equal(lineType, await LookupClient().LookupLineTypeAsync(Phone, Ct));
    }

    [Fact]
    public async Task Lookup_failure_returns_null()
    {
        server.Given(Request.Create().UsingGet()).RespondWith(Json(500, "{}"));

        Assert.Null(await LookupClient().LookupLineTypeAsync(Phone, Ct));
    }

    [Theory]
    [InlineData("""{"success":true,"action":"phone_start","hostname":"hockeyindex.com"}""", true)]
    [InlineData("""{"success":false,"error-codes":["invalid-input-response"]}""", false)]
    [InlineData("""{"success":true,"action":"email_start","hostname":"hockeyindex.com"}""", false)]
    [InlineData("""{"success":true,"action":"phone_start","hostname":"evil.example"}""", false)]
    public async Task Turnstile_requires_success_action_and_hostname(string body, bool expected)
    {
        Respond("/turnstile/v0/siteverify", 200, body);

        Assert.Equal(expected, await TurnstileClient().VerifyAsync("token", TurnstileActions.PhoneStart, IPAddress.Parse("203.0.113.10"), Ct));
    }

    [Fact]
    public async Task Turnstile_sends_secret_token_and_remote_ip()
    {
        Respond("/turnstile/v0/siteverify", 200, """{"success":true,"action":"phone_start","hostname":"hockeyindex.com"}""");

        await TurnstileClient().VerifyAsync("tok", TurnstileActions.PhoneStart, IPAddress.Parse("203.0.113.10"), Ct);

        var body = Assert.Single(server.LogEntries).RequestMessage!.Body;
        Assert.Contains("secret=ts-secret", body, StringComparison.Ordinal);
        Assert.Contains("response=tok", body, StringComparison.Ordinal);
        Assert.Contains("remoteip=203.0.113.10", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Turnstile_rejects_missing_token_without_calling_cloudflare()
    {
        Assert.False(await TurnstileClient().VerifyAsync(" ", TurnstileActions.PhoneStart, IPAddress.Loopback, Ct));
        Assert.Empty(server.LogEntries);
    }

    [Fact]
    public async Task Postmark_sends_server_token_and_message()
    {
        Respond("/email", 200, """{"ErrorCode":0}""");
        var sender = new PostmarkEmailSender(
            http,
            Options.Create(new PostmarkOptions { ServerToken = "pm-token", BaseUrl = BaseUrl }),
            NullLogger<PostmarkEmailSender>.Instance);

        Assert.True(await sender.SendAsync(new EmailMessage("a@example.com", "Subject", "Body"), Ct));

        var request = Assert.Single(server.LogEntries).RequestMessage!;
        Assert.Equal("pm-token", request.Headers!["X-Postmark-Server-Token"].Single());
        Assert.Contains("\"To\":\"a@example.com\"", request.Body, StringComparison.Ordinal);
        Assert.Contains("\"TextBody\":\"Body\"", request.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("+12065550001", PhoneLineTypes.Landline)]
    [InlineData("+12065550002", PhoneLineTypes.NonFixedVoip)]
    [InlineData("+12065550003", PhoneLineTypes.TollFree)]
    [InlineData("+12065551234", PhoneLineTypes.Mobile)]
    public async Task Fake_lookup_follows_documented_suffixes(string phone, string expected)
    {
        Assert.Equal(expected, await new FakePhoneLineTypeLookup().LookupLineTypeAsync(phone, Ct));
    }

    [Fact]
    public async Task Fake_sms_accepts_only_the_documented_code_once_per_start()
    {
        var sms = new FakeSmsVerifier();
        await sms.StartAsync(Phone, Ct);

        Assert.False(await sms.CheckAsync(Phone, "000000", Ct));
        Assert.True(await sms.CheckAsync(Phone, FakeSmsVerifier.Code, Ct));
        Assert.False(await sms.CheckAsync(Phone, FakeSmsVerifier.Code, Ct));
    }

    public void Dispose()
    {
        http.Dispose();
        server.Dispose();
    }

    private TwilioVerifyClient VerifyClient() => new(http, TwilioOptions(), NullLogger<TwilioVerifyClient>.Instance);

    private TwilioLookupClient LookupClient() => new(http, TwilioOptions(), NullLogger<TwilioLookupClient>.Instance);

    private TurnstileVerifier TurnstileClient() => new(
        http,
        Options.Create(new TurnstileOptions { SecretKey = "ts-secret", ExpectedHostname = "hockeyindex.com", BaseUrl = BaseUrl }),
        NullLogger<TurnstileVerifier>.Instance);

    private IOptions<TwilioOptions> TwilioOptions() => Options.Create(new TwilioOptions
    {
        AccountSid = "AC123",
        AuthToken = "secret",
        VerifyServiceSid = "VA123",
        VerifyBaseUrl = BaseUrl,
        LookupBaseUrl = BaseUrl,
    });

    private void Respond(string path, int status, string body) =>
        server.Given(Request.Create().WithPath(path).UsingPost()).RespondWith(Json(status, body));

    private static IResponseBuilder Json(int status, string body) =>
        Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json").WithBody(body);
}
