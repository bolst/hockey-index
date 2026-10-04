using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace HockeyIndex.Api.Integrations.WebRisk;

public sealed class WebRiskOptions
{
    public const string SectionName = "WebRisk";

    public string? ApiKey { get; set; }

    public Uri BaseUrl { get; set; } = new("https://webrisk.googleapis.com/");

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>Google Web Risk Lookup API (<c>uris.search</c>). The key travels in a header so it never appears in logged URLs.</summary>
public sealed partial class WebRiskClient(HttpClient http, IOptions<WebRiskOptions> options, ILogger<WebRiskClient> logger) : IUrlReputationProvider
{
    public const string ApiKeyHeader = "X-Goog-Api-Key";

    private static readonly string ThreatTypesQuery = string.Join(
        '&', new[] { "MALWARE", "SOCIAL_ENGINEERING", "UNWANTED_SOFTWARE" }.Select(type => $"threatTypes={type}"));

    public async Task<UrlReputation> LookupAsync(Uri url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);

        var settings = options.Value;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.Timeout);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(settings.BaseUrl, $"v1/uris:search?{ThreatTypesQuery}&uri={Uri.EscapeDataString(url.AbsoluteUri)}"));
        request.Headers.Add(ApiKeyHeader, settings.ApiKey);

        try
        {
            using var response = await http.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                LogLookupFailed(logger, (int)response.StatusCode);
                return UrlReputation.Failed(isProviderFault: response.StatusCode != HttpStatusCode.BadRequest);
            }

            var body = await response.Content.ReadFromJsonAsync<SearchUrisResponse>(timeout.Token);
            return body?.Threat is { ThreatTypes.Count: > 0 } threat
                ? UrlReputation.Malicious(threat.ThreatTypes, threat.ExpireTime)
                : UrlReputation.Safe();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogLookupTimedOut(logger);
            return UrlReputation.Failed(isProviderFault: true);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            LogLookupError(logger, exception);
            return UrlReputation.Failed(isProviderFault: true);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Web Risk lookup failed with status {StatusCode}")]
    private static partial void LogLookupFailed(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Web Risk lookup timed out")]
    private static partial void LogLookupTimedOut(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Web Risk lookup failed")]
    private static partial void LogLookupError(ILogger logger, Exception exception);

    private sealed record SearchUrisResponse(ThreatUri? Threat);

    private sealed record ThreatUri(IReadOnlyList<string> ThreatTypes, DateTimeOffset? ExpireTime);
}

public static class WebRiskSetup
{
    public static IServiceCollection AddWebRiskClient(this IServiceCollection services)
    {
        services.AddOptions<WebRiskOptions>()
            .BindConfiguration(WebRiskOptions.SectionName)
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.ApiKey), $"{WebRiskOptions.SectionName}:ApiKey is required.")
            .ValidateOnStart();
        services.AddHttpClient<IUrlReputationProvider, WebRiskClient>()
            .RedactLoggedHeaders([WebRiskClient.ApiKeyHeader]);
        return services;
    }
}
