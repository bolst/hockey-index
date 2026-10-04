using System.Net.Http.Headers;
using HockeyIndex.Api.Infrastructure.Caching;
using HockeyIndex.Api.Infrastructure.Observability;
using Microsoft.Extensions.Options;

namespace HockeyIndex.Api.Integrations.Cloudflare;

public static class CachePurgeModes
{
    /// <summary>Purge by <c>Cache-Tag: event-{publicId}</c> (default).</summary>
    public const string Tag = "tag";

    /// <summary>Fallback when the zone plan lacks tag purge: purge <c>{PublicApiBaseUrl}v1/events/{publicId}</c>.</summary>
    public const string Url = "url";
}

public static class CachePurgeResults
{
    public const string Success = "success";
    public const string Error = "error";
}

public sealed class CloudflareOptions
{
    public const string SectionName = "Cloudflare";

    public string ApiToken { get; set; } = string.Empty;
    public string ZoneId { get; set; } = string.Empty;

    /// <summary><see cref="CachePurgeModes.Tag"/> or <see cref="CachePurgeModes.Url"/>.</summary>
    public string PurgeMode { get; set; } = CachePurgeModes.Tag;

    public Uri BaseUrl { get; set; } = new("https://api.cloudflare.com/");

    /// <summary>Origin of the public API, used to build purge URLs in <see cref="CachePurgeModes.Url"/> mode.</summary>
    public Uri PublicApiBaseUrl { get; set; } = new("https://api.hockeyindex.com/");

    public int MaxRetries { get; set; } = 3;

    /// <summary>Delay before retry n is n × this value.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
}

public sealed partial class CloudflareCachePurger(
    HttpClient http, IOptions<CloudflareOptions> options, HockeyIndexMetrics metrics, ILogger<CloudflareCachePurger> logger) : ICachePurger
{
    /// <summary>Cloudflare accepts at most 30 files per purge call; tags use the same chunk size for simplicity.</summary>
    public const int MaxItemsPerCall = 30;

    private readonly CloudflareOptions settings = options.Value;

    public static string TagFor(string eventPublicId) => $"event-{eventPublicId}";

    public async Task<bool> PurgeAsync(IReadOnlyCollection<string> eventPublicIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(eventPublicIds);

        var succeeded = true;
        foreach (var chunk in eventPublicIds.Chunk(MaxItemsPerCall))
        {
            succeeded &= await PurgeChunkAsync(chunk, cancellationToken);
        }

        return succeeded;
    }

    private async Task<bool> PurgeChunkAsync(string[] eventPublicIds, CancellationToken cancellationToken)
    {
        for (var retry = 0; ; retry++)
        {
            if (await TrySendAsync(eventPublicIds, cancellationToken))
            {
                metrics.RecordCachePurge(CachePurgeResults.Success);
                return true;
            }

            if (retry >= settings.MaxRetries)
            {
                metrics.RecordCachePurge(CachePurgeResults.Error);
                LogPurgeGaveUp(logger, eventPublicIds.Length, retry + 1);
                return false;
            }

            await Task.Delay(settings.RetryDelay * (retry + 1), cancellationToken);
        }
    }

    private async Task<bool> TrySendAsync(string[] eventPublicIds, CancellationToken cancellationToken)
    {
        object body = settings.PurgeMode == CachePurgeModes.Url
            ? new { files = eventPublicIds.Select(id => new Uri(settings.PublicApiBaseUrl, $"v1/events/{id}").AbsoluteUri).ToArray() }
            : new { tags = eventPublicIds.Select(TagFor).ToArray() };

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(settings.BaseUrl, $"client/v4/zones/{settings.ZoneId}/purge_cache"))
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiToken);

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LogPurgeAttemptFailed(logger, (int)response.StatusCode);
            }

            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException exception)
        {
            LogPurgeAttemptError(logger, exception);
            return false;
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogPurgeAttemptError(logger, exception);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cloudflare purge attempt failed with status {StatusCode}")]
    private static partial void LogPurgeAttemptFailed(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cloudflare purge attempt failed")]
    private static partial void LogPurgeAttemptError(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Cloudflare purge of {Count} events failed after {Attempts} attempts")]
    private static partial void LogPurgeGaveUp(ILogger logger, int count, int attempts);
}

public static class CloudflareSetup
{
    public static IServiceCollection AddCloudflareCachePurger(this IServiceCollection services)
    {
        services.AddOptions<CloudflareOptions>()
            .BindConfiguration(CloudflareOptions.SectionName)
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.ApiToken), $"{CloudflareOptions.SectionName}:ApiToken is required.")
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.ZoneId), $"{CloudflareOptions.SectionName}:ZoneId is required.")
            .Validate(
                settings => settings.PurgeMode is CachePurgeModes.Tag or CachePurgeModes.Url,
                $"{CloudflareOptions.SectionName}:PurgeMode must be '{CachePurgeModes.Tag}' or '{CachePurgeModes.Url}'.")
            .ValidateOnStart();
        services.AddHttpClient<ICachePurger, CloudflareCachePurger>(client => client.Timeout = TimeSpan.FromSeconds(10))
            .RedactLoggedHeaders(["Authorization"]);
        return services;
    }
}
