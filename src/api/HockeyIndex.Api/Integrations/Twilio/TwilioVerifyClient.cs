using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace HockeyIndex.Api.Integrations.Twilio;

public sealed partial class TwilioVerifyClient(HttpClient http, IOptions<TwilioOptions> options, ILogger<TwilioVerifyClient> logger) : ISmsVerifier
{
    public async Task<bool> StartAsync(string e164, CancellationToken cancellationToken)
    {
        using var response = await PostAsync("Verifications", [new("To", e164), new("Channel", "sms")], cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            LogFailure(logger, "start", (int)response.StatusCode);
        }

        return response.IsSuccessStatusCode;
    }

    public async Task<bool> CheckAsync(string e164, string code, CancellationToken cancellationToken)
    {
        using var response = await PostAsync("VerificationCheck", [new("To", e164), new("Code", code)], cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // Twilio answers 404 once a verification is approved, expired, or out of attempts.
            LogFailure(logger, "check", (int)response.StatusCode);
            return false;
        }

        var check = await response.Content.ReadFromJsonAsync<VerificationCheck>(cancellationToken);
        return check?.Status == "approved";
    }

    private async Task<HttpResponseMessage> PostAsync(string resource, KeyValuePair<string, string>[] form, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(settings.VerifyBaseUrl, $"v2/Services/{settings.VerifyServiceSid}/{resource}"))
        {
            Content = new FormUrlEncodedContent(form),
        };
        request.Headers.Authorization = TwilioAuth.Basic(settings);
        return await http.SendAsync(request, cancellationToken);
    }

    private sealed record VerificationCheck([property: JsonPropertyName("status")] string? Status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Twilio Verify {Operation} failed with status {StatusCode}")]
    private static partial void LogFailure(ILogger logger, string operation, int statusCode);
}

internal static class TwilioAuth
{
    public static AuthenticationHeaderValue Basic(TwilioOptions options) =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.AccountSid}:{options.AuthToken}")));
}
