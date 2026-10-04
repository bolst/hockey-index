using System.Net;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace HockeyIndex.Api.Integrations.Turnstile;

public static class TurnstileActions
{
    public const string PhoneStart = "phone_start";
    public const string EmailStart = "email_start";
    public const string Report = "report";
}

public interface ITurnstileVerifier
{
    Task<bool> VerifyAsync(string? token, string expectedAction, IPAddress remoteIp, CancellationToken cancellationToken);
}

public sealed class TurnstileOptions
{
    public const string SectionName = "Turnstile";

    public string SecretKey { get; set; } = "";

    /// <summary>When set, the siteverify <c>hostname</c> must match (e.g. <c>hockeyindex.com</c>).</summary>
    public string? ExpectedHostname { get; set; }

    public Uri BaseUrl { get; set; } = new("https://challenges.cloudflare.com/");
}

public sealed partial class TurnstileVerifier(HttpClient http, IOptions<TurnstileOptions> options, ILogger<TurnstileVerifier> logger) : ITurnstileVerifier
{
    public async Task<bool> VerifyAsync(string? token, string expectedAction, IPAddress remoteIp, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(remoteIp);
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var settings = options.Value;
        using var content = new FormUrlEncodedContent(
        [
            new("secret", settings.SecretKey),
            new("response", token),
            new("remoteip", remoteIp.ToString()),
        ]);
        using var response = await http.PostAsync(new Uri(settings.BaseUrl, "turnstile/v0/siteverify"), content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            LogFailure(logger, (int)response.StatusCode);
            return false;
        }

        var result = await response.Content.ReadFromJsonAsync<SiteverifyResponse>(cancellationToken);
        return result is { Success: true }
            && result.Action == expectedAction
            && (string.IsNullOrEmpty(settings.ExpectedHostname) || string.Equals(result.Hostname, settings.ExpectedHostname, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record SiteverifyResponse(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("action")] string? Action,
        [property: JsonPropertyName("hostname")] string? Hostname);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Turnstile siteverify failed with status {StatusCode}")]
    private static partial void LogFailure(ILogger logger, int statusCode);
}
