using System.Collections.Concurrent;
using System.Net;
using HockeyIndex.Api.Integrations.Email;
using HockeyIndex.Api.Integrations.Turnstile;
using HockeyIndex.Api.Integrations.Twilio;

namespace HockeyIndex.Api.Integrations.Fakes;

/// <summary>
/// Twilio Verify fake. Every number accepts the code <see cref="Code"/> (<c>123456</c>) after a start; any other code fails.
/// Started and checked numbers are recorded so tests can assert that no SMS was sent.
/// </summary>
public sealed class FakeSmsVerifier : ISmsVerifier
{
    public const string Code = "123456";

    private readonly ConcurrentQueue<string> started = new();
    private readonly ConcurrentDictionary<string, byte> pending = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> StartedNumbers => started;

    public Task<bool> StartAsync(string e164, CancellationToken cancellationToken)
    {
        started.Enqueue(e164);
        pending[e164] = 0;
        return Task.FromResult(true);
    }

    public Task<bool> CheckAsync(string e164, string code, CancellationToken cancellationToken) =>
        Task.FromResult(code == Code && pending.TryRemove(e164, out _));
}

/// <summary>
/// Twilio Lookup fake. Line type by the last four digits: <c>0001</c> landline, <c>0002</c> nonFixedVoip,
/// <c>0003</c> tollFree, <c>0004</c> fixedVoip; anything else is mobile. <see cref="SetLineType"/> overrides per number.
/// </summary>
public sealed class FakePhoneLineTypeLookup : IPhoneLineTypeLookup
{
    private readonly ConcurrentDictionary<string, string> overrides = new(StringComparer.Ordinal);
    private int lookups;

    public int LookupCount => Volatile.Read(ref lookups);

    public void SetLineType(string e164, string lineType) => overrides[e164] = lineType;

    public Task<string?> LookupLineTypeAsync(string e164, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(e164);
        Interlocked.Increment(ref lookups);
        if (overrides.TryGetValue(e164, out var lineType))
        {
            return Task.FromResult<string?>(lineType);
        }

        return Task.FromResult<string?>(e164[^4..] switch
        {
            "0001" => PhoneLineTypes.Landline,
            "0002" => PhoneLineTypes.NonFixedVoip,
            "0003" => PhoneLineTypes.TollFree,
            "0004" => PhoneLineTypes.FixedVoip,
            _ => PhoneLineTypes.Mobile,
        });
    }
}

/// <summary>Turnstile fake: any non-empty token passes unless it starts with <c>fail</c>.</summary>
public sealed class FakeTurnstileVerifier : ITurnstileVerifier
{
    public const string FailingTokenPrefix = "fail";

    public Task<bool> VerifyAsync(string? token, string expectedAction, IPAddress remoteIp, CancellationToken cancellationToken) =>
        Task.FromResult(!string.IsNullOrWhiteSpace(token) && !token.StartsWith(FailingTokenPrefix, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Postmark fake: records messages in memory instead of sending them.</summary>
public sealed class FakeEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> sent = new();

    public IReadOnlyCollection<EmailMessage> Sent => sent;

    public Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        sent.Enqueue(message);
        return Task.FromResult(true);
    }
}
