using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace HockeyIndex.Api.Integrations.Geocoding;

public sealed class MapboxOptions
{
    public const string SectionName = "Mapbox";

    public string? AccessToken { get; set; }

    public Uri BaseUrl { get; set; } = new("https://api.mapbox.com/");

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// Mapbox Geocoding v6 forward search limited to US/CA addresses. Suggestions use <c>permanent=false</c>; the single lookup
/// on select uses <c>permanent=true</c>, the only mode whose results Mapbox lets us store.
/// </summary>
public sealed partial class MapboxGeocoder(HttpClient http, IOptions<MapboxOptions> options, ILogger<MapboxGeocoder> logger) : IGeocoder
{
    public const int SuggestionLimit = 5;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public Task<GeocodeResult> SuggestAsync(string query, GeoCoordinate? proximity, CancellationToken cancellationToken) =>
        ForwardAsync(query, proximity, permanent: false, SuggestionLimit, cancellationToken);

    public Task<GeocodeResult> GeocodePermanentAsync(string fullAddress, CancellationToken cancellationToken) =>
        ForwardAsync(fullAddress, proximity: null, permanent: true, limit: 1, cancellationToken);

    private async Task<GeocodeResult> ForwardAsync(
        string query, GeoCoordinate? proximity, bool permanent, int limit, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var settings = options.Value;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.Timeout);

        var parameters = new List<string>
        {
            $"q={Uri.EscapeDataString(query)}",
            "country=us,ca",
            "types=address",
            $"limit={limit}",
            $"autocomplete={(permanent ? "false" : "true")}",
            $"permanent={(permanent ? "true" : "false")}",
        };
        if (proximity is not null)
        {
            parameters.Add(string.Create(CultureInfo.InvariantCulture, $"proximity={proximity.Longitude:R},{proximity.Latitude:R}"));
        }

        parameters.Add($"access_token={Uri.EscapeDataString(settings.AccessToken ?? string.Empty)}");
        using var request = new HttpRequestMessage(
            HttpMethod.Get, new Uri(settings.BaseUrl, $"search/geocode/v6/forward?{string.Join('&', parameters)}"));

        try
        {
            using var response = await http.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                LogRequestFailed(logger, (int)response.StatusCode, permanent);
                return GeocodeResult.Failed(isProviderFault: response.StatusCode is not (HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity));
            }

            var body = await response.Content.ReadFromJsonAsync<FeatureCollection>(JsonOptions, timeout.Token);
            return GeocodeResult.Found((body?.Features ?? []).Select(ToAddress).OfType<GeocodedAddress>().ToList());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogRequestTimedOut(logger, permanent);
            return GeocodeResult.Failed(isProviderFault: true);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            LogRequestError(logger, exception, permanent);
            return GeocodeResult.Failed(isProviderFault: true);
        }
    }

    /// <returns><c>null</c> when the feature lacks a field a venue needs.</returns>
    private static GeocodedAddress? ToAddress(Feature feature)
    {
        var properties = feature.Properties;
        var context = properties?.Context;
        var city = context?.Place?.Name ?? context?.Locality?.Name;
        var addressLine = context?.Address?.Name ?? properties?.Name;
        var region = context?.Region?.RegionCode;
        var country = context?.Country?.CountryCode;
        if (properties is not { MapboxId: { Length: > 0 } id, FullAddress: { Length: > 0 } fullAddress, Coordinates: { } coordinates }
            || string.IsNullOrWhiteSpace(city)
            || string.IsNullOrWhiteSpace(addressLine)
            || region is not { Length: 2 }
            || country is not { Length: 2 })
        {
            return null;
        }

        return new GeocodedAddress(
            id,
            fullAddress,
            addressLine,
            city,
            region.ToUpperInvariant(),
            country.ToUpperInvariant(),
            coordinates.Latitude,
            coordinates.Longitude);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Mapbox geocode failed with status {StatusCode} (permanent: {Permanent})")]
    private static partial void LogRequestFailed(ILogger logger, int statusCode, bool permanent);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Mapbox geocode timed out (permanent: {Permanent})")]
    private static partial void LogRequestTimedOut(ILogger logger, bool permanent);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Mapbox geocode failed (permanent: {Permanent})")]
    private static partial void LogRequestError(ILogger logger, Exception exception, bool permanent);

    private sealed record FeatureCollection(IReadOnlyList<Feature>? Features);

    private sealed record Feature(FeatureProperties? Properties);

    private sealed record FeatureProperties(
        string? MapboxId, string? Name, string? FullAddress, Coordinates? Coordinates, FeatureContext? Context);

    private sealed record Coordinates(double Latitude, double Longitude);

    private sealed record FeatureContext(
        NamedContext? Address, NamedContext? Place, NamedContext? Locality, RegionContext? Region, CountryContext? Country);

    private sealed record NamedContext(string? Name);

    private sealed record RegionContext(string? RegionCode);

    private sealed record CountryContext(string? CountryCode);
}

public static class MapboxSetup
{
    public static IServiceCollection AddMapboxGeocoder(this IServiceCollection services)
    {
        services.AddOptions<MapboxOptions>()
            .BindConfiguration(MapboxOptions.SectionName)
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.AccessToken), $"{MapboxOptions.SectionName}:AccessToken is required.")
            .ValidateOnStart();
        services.AddHttpClient<IGeocoder, MapboxGeocoder>();
        return services;
    }
}
