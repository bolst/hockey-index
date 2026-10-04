using System.Security.Cryptography;
using System.Text;
using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Infrastructure.RateLimiting;
using HockeyIndex.Api.Integrations.Twilio;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using NodaTime;

namespace HockeyIndex.Api.Features.Safety;

/// <summary>
/// Validates <c>X-Twilio-Signature</c>: Base64(HMAC-SHA1(auth token, URL + each form key and value sorted by key)).
/// The URL is always the configured public <c>Twilio:CallbackUrl</c>, never one rebuilt from the request, which arrives
/// through the tunnel with an internal Host header.
/// </summary>
public static class TwilioSignatureValidator
{
    public const string HeaderName = "X-Twilio-Signature";

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA5350:Do Not Use Weak Cryptographic Algorithms",
        Justification = "Twilio defines X-Twilio-Signature as HMAC-SHA1; HMAC-SHA1 remains a sound MAC.")]
    public static string Compute(string authToken, string url, IEnumerable<KeyValuePair<string, StringValues>> form)
    {
        ArgumentNullException.ThrowIfNull(form);
        var data = new StringBuilder(url);
        foreach (var (key, values) in form.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            foreach (var value in values.Order(StringComparer.Ordinal))
            {
                data.Append(key).Append(value);
            }
        }

        var hash = HMACSHA1.HashData(Encoding.UTF8.GetBytes(authToken), Encoding.UTF8.GetBytes(data.ToString()));
        return Convert.ToBase64String(hash);
    }

    public static bool IsValid(string? signature, string authToken, string url, IEnumerable<KeyValuePair<string, StringValues>> form)
    {
        if (string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(authToken) || string.IsNullOrEmpty(url))
        {
            return false;
        }

        var expected = Encoding.UTF8.GetBytes(Compute(authToken, url, form));
        return CryptographicOperations.FixedTimeEquals(expected, Encoding.UTF8.GetBytes(signature));
    }
}

/// <summary>
/// POST /v1/internal/twilio-usage (AC-OP-4): a signed Twilio usage-trigger callback opens the <c>otp_sms</c> breaker until the
/// next UTC midnight. Anonymous and outside the credentialed (CSRF-guarded) paths; the signature is the only check.
/// </summary>
public sealed class TwilioUsageWebhook(BreakerService breakers, IOptions<TwilioOptions> options, IClock clock)
{
    public const string BreakerReason = "twilio_usage_trigger";

    public async Task<IResult> HandleAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var form = request.HasFormContentType ? await request.ReadFormAsync(cancellationToken) : FormCollection.Empty;
        var twilio = options.Value;
        if (!TwilioSignatureValidator.IsValid(request.Headers[TwilioSignatureValidator.HeaderName], twilio.AuthToken, twilio.CallbackUrl, form))
        {
            return SafetyProblems.InvalidSignature();
        }

        var now = clock.GetCurrentInstant().ToDateTimeOffset();
        var nextMidnight = new DateTimeOffset(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);
        await breakers.OpenAsync(BreakerNames.OtpSms, nextMidnight, BreakerReason, BreakerOpener.TwilioWebhook, cancellationToken);
        return TypedResults.NoContent();
    }
}
