namespace HockeyIndex.Api.Domain;

public static class ProviderNames
{
    public const string MapboxTemp = "mapbox_temp";
    public const string MapboxPerm = "mapbox_perm";
    public const string WebRisk = "webrisk";
    public const string TwilioVerify = "twilio_verify";
    public const string TwilioLookup = "twilio_lookup";
}

public sealed class ProviderUsage
{
    public static readonly Guid GlobalHostId = Guid.Empty;

    public required string Provider { get; init; }
    public required Guid HostId { get; init; }
    public required DateOnly Day { get; init; }
    public int Calls { get; set; }
    public int Cap { get; set; }
}
