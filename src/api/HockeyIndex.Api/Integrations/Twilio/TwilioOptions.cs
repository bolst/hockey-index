namespace HockeyIndex.Api.Integrations.Twilio;

public sealed class TwilioOptions
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = "";
    public string AuthToken { get; set; } = "";
    public string VerifyServiceSid { get; set; } = "";

    /// <summary>Public URL Twilio signs usage-trigger callbacks with (validated in M6).</summary>
    public string CallbackUrl { get; set; } = "";

    public Uri VerifyBaseUrl { get; set; } = new("https://verify.twilio.com/");
    public Uri LookupBaseUrl { get; set; } = new("https://lookups.twilio.com/");
}
