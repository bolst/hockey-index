using System.Net;
using System.Net.Http.Json;
using HockeyIndex.Api.Integrations;
using HockeyIndex.Api.Tests.Integration.Infrastructure;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using static HockeyIndex.Api.Tests.Integration.Infrastructure.AuthTestClient;

namespace HockeyIndex.Api.Tests.Integration;

/// <summary>Runs the real Twilio, Turnstile, and Postmark clients against WireMock.</summary>
public sealed class AuthProviderWireMockTests(PostgisFixture postgis) : IntegrationTest(postgis), IDisposable
{
    private const string VerificationsPath = "/v2/Services/VA-test/Verifications";
    private const string ChecksPath = "/v2/Services/VA-test/VerificationCheck";
    private const string SiteVerifyPath = "/turnstile/v0/siteverify";

    private readonly WireMockServer server = WireMockServer.Start();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Failed_turnstile_never_reaches_twilio()
    {
        StubTurnstile(success: false);
        StubTwilio(lineType: "mobile");
        using var client = RealProviders().CreateHostClient();

        using var response = await client.StartPhoneAsync(Phone(1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, Calls(SiteVerifyPath));
        Assert.Equal(0, Calls("/v2/PhoneNumbers/*"));
        Assert.Equal(0, Calls(VerificationsPath));
    }

    [Fact]
    public async Task Landline_lookup_stops_before_verify()
    {
        StubTurnstile(success: true);
        StubTwilio(lineType: "landline");
        using var client = RealProviders().CreateHostClient();

        using var response = await client.StartPhoneAsync(Phone(1));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(1, Calls("/v2/PhoneNumbers/*"));
        Assert.Equal(0, Calls(VerificationsPath));
    }

    [Fact]
    public async Task Voip_number_is_sent_verified_and_flagged()
    {
        StubTurnstile(success: true);
        StubTwilio(lineType: "nonFixedVoip");
        using var client = RealProviders().CreateHostClient();

        using var start = await client.StartPhoneAsync(Phone(1));
        using var verify = await client.VerifyPhoneAsync(Phone(1), "424242");

        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        Assert.Equal(1, Calls(VerificationsPath));
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        Assert.True((await ReadJsonAsync(verify)).GetProperty("isVoip").GetBoolean());
    }

    [Fact]
    public async Task Email_confirmation_goes_through_postmark()
    {
        StubTurnstile(success: true);
        StubTwilio(lineType: "mobile");
        server.Given(Request.Create().WithPath("/email").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody("""{"ErrorCode":0}"""));
        using var client = RealProviders().CreateHostClient();
        await client.SignInWithPhoneAsync(Phone(1), "424242");

        using var response = await client.PostAsJsonAsync("/v1/me/email", new { email = "jordan@example.com" }, Ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var request = Assert.Single(server.LogEntries, entry => entry.RequestMessage!.Path == "/email").RequestMessage!;
        Assert.Equal("pm-test", request.Headers!["X-Postmark-Server-Token"].Single());
        Assert.Contains("\"To\":\"jordan@example.com\"", request.Body, StringComparison.Ordinal);
    }

    public void Dispose() => server.Dispose();

    private ApiFactory RealProviders()
    {
        var url = server.Url + "/";
        return CreateFactory(
            new Dictionary<string, string?>
            {
                ["Twilio:AccountSid"] = "AC-test",
                ["Twilio:AuthToken"] = "token",
                ["Twilio:VerifyServiceSid"] = "VA-test",
                ["Twilio:VerifyBaseUrl"] = url,
                ["Twilio:LookupBaseUrl"] = url,
                ["Turnstile:SecretKey"] = "ts-test",
                ["Turnstile:ExpectedHostname"] = "hockeyindex.com",
                ["Turnstile:BaseUrl"] = url,
                ["Postmark:ServerToken"] = "pm-test",
                ["Postmark:BaseUrl"] = url,
            },
            services => services.AddIdentityProviders());
    }

    private void StubTurnstile(bool success) =>
        server.Given(Request.Create().WithPath(SiteVerifyPath).UsingPost())
            .RespondWith(Json(success
                ? """{"success":true,"action":"phone_start","hostname":"hockeyindex.com"}"""
                : """{"success":false,"error-codes":["invalid-input-response"]}"""));

    private void StubTwilio(string lineType)
    {
        server.Given(Request.Create().WithPath(new WildcardMatcher("/v2/PhoneNumbers/*")).UsingGet())
            .RespondWith(Json($$$"""{"line_type_intelligence":{"type":"{{{lineType}}}"}}"""));
        server.Given(Request.Create().WithPath(VerificationsPath).UsingPost())
            .RespondWith(Json("""{"status":"pending"}""", 201));
        server.Given(Request.Create().WithPath(ChecksPath).UsingPost().WithBody(new RegexMatcher("Code=424242")))
            .RespondWith(Json("""{"status":"approved"}"""));
    }

    private int Calls(string pathPattern) =>
        server.LogEntries.Count(entry => new WildcardMatcher(pathPattern).IsMatch(entry.RequestMessage!.Path).IsPerfect());

    private static IResponseBuilder Json(string body, int status = 200) =>
        Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json").WithBody(body);
}
