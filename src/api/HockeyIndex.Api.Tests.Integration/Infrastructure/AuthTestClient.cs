using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using HockeyIndex.Api.Infrastructure.Auth;
using HockeyIndex.Api.Infrastructure.Http;
using HockeyIndex.Api.Integrations.Email;
using HockeyIndex.Api.Integrations.Fakes;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HockeyIndex.Api.Tests.Integration.Infrastructure;

/// <summary>SPA-like client: HTTPS (so the Secure <c>__Host-</c> cookie round-trips), cookies, CSRF header, and a client IP.</summary>
public static partial class AuthTestClient
{
    public const string PassingTurnstileToken = "turnstile-ok";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A valid US mobile number per the fake Lookup (never ends in 0001-0004).</summary>
    public static string Phone(int n) => $"+1206412{1000 + n:D4}";

    public static HttpClient CreateHostClient(this ApiFactory factory, string clientIp = ApiFactory.ClientIp, bool withRequestedWith = true)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
        client.DefaultRequestHeaders.Add(ClientIpResolver.HeaderName, clientIp);
        if (withRequestedWith)
        {
            client.DefaultRequestHeaders.Add(RequestedWithGuard.HeaderName, RequestedWithGuard.HeaderValue);
        }

        return client;
    }

    public static Task<HttpResponseMessage> StartPhoneAsync(this HttpClient client, string phone, string turnstileToken = PassingTurnstileToken) =>
        client.PostAsJsonAsync("/v1/auth/phone/start", new { phone, turnstileToken }, Ct);

    public static Task<HttpResponseMessage> VerifyPhoneAsync(this HttpClient client, string phone, string code = FakeSmsVerifier.Code) =>
        client.PostAsJsonAsync("/v1/auth/phone/verify", new { phone, code }, Ct);

    /// <returns>The signed-in host's id.</returns>
    public static async Task<Guid> SignInWithPhoneAsync(this HttpClient client, string phone, string code = FakeSmsVerifier.Code)
    {
        using (var start = await client.StartPhoneAsync(phone))
        {
            Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        }

        using var verify = await client.VerifyPhoneAsync(phone, code);
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        return (await ReadJsonAsync(verify)).GetProperty("id").GetGuid();
    }

    public static Task<HttpResponseMessage> GetMeAsync(this HttpClient client) =>
        client.GetAsync(new Uri("/v1/me", UriKind.Relative), Ct);

    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return document.RootElement.Clone();
    }

    public static async Task<string?> ProblemCodeAsync(HttpResponseMessage response) =>
        (await ReadJsonAsync(response)).TryGetProperty("code", out var code) ? code.GetString() : null;

    public static string CodeFrom(EmailMessage message) => CodePattern().Match(message.TextBody).Groups[1].Value;

    public static string LinkTokenFrom(EmailMessage message) => LinkTokenPattern().Match(message.TextBody).Groups[1].Value;

    [GeneratedRegex(@"code(?: is|:) (\d{6})")]
    private static partial Regex CodePattern();

    [GeneratedRegex(@"#t=(\S+)")]
    private static partial Regex LinkTokenPattern();
}
