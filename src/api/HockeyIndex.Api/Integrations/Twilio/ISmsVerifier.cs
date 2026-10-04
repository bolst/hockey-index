namespace HockeyIndex.Api.Integrations.Twilio;

/// <summary>Sends and checks SMS one-time codes (Twilio Verify).</summary>
public interface ISmsVerifier
{
    Task<bool> StartAsync(string e164, CancellationToken cancellationToken);

    Task<bool> CheckAsync(string e164, string code, CancellationToken cancellationToken);
}

/// <summary>Line type of a phone number (Twilio Lookup v2 <c>line_type_intelligence.type</c>).</summary>
public interface IPhoneLineTypeLookup
{
    /// <returns>The provider's line type, or <c>null</c> when the lookup failed.</returns>
    Task<string?> LookupLineTypeAsync(string e164, CancellationToken cancellationToken);
}

public static class PhoneLineTypes
{
    public const string Mobile = "mobile";
    public const string Landline = "landline";
    public const string FixedVoip = "fixedVoip";
    public const string NonFixedVoip = "nonFixedVoip";
    public const string TollFree = "tollFree";

    /// <summary>Open question Q3 default: numbers that cannot receive SMS are rejected rather than offered voice.</summary>
    public static bool IsRejected(string lineType) => lineType is Landline or TollFree;

    public static bool IsVoip(string lineType) => lineType is FixedVoip or NonFixedVoip;
}
