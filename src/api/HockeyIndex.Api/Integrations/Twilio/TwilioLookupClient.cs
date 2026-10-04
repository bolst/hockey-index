using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace HockeyIndex.Api.Integrations.Twilio;

public sealed partial class TwilioLookupClient(HttpClient http, IOptions<TwilioOptions> options, ILogger<TwilioLookupClient> logger) : IPhoneLineTypeLookup
{
    public async Task<string?> LookupLineTypeAsync(string e164, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var uri = new Uri(settings.LookupBaseUrl, $"v2/PhoneNumbers/{Uri.EscapeDataString(e164)}?Fields=line_type_intelligence");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = TwilioAuth.Basic(settings);

        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            LogFailure(logger, (int)response.StatusCode);
            return null;
        }

        var body = await response.Content.ReadFromJsonAsync<LookupResponse>(cancellationToken);
        return body?.LineTypeIntelligence?.Type;
    }

    private sealed record LookupResponse([property: JsonPropertyName("line_type_intelligence")] LineTypeIntelligence? LineTypeIntelligence);

    private sealed record LineTypeIntelligence([property: JsonPropertyName("type")] string? Type);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Twilio Lookup failed with status {StatusCode}")]
    private static partial void LogFailure(ILogger logger, int statusCode);
}
