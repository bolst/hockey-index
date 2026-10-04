namespace HockeyIndex.Api.Domain;

public static class UrlVerdictValues
{
    public const string Safe = "safe";
    public const string Malicious = "malicious";
}

public sealed class UrlVerdict
{
    public required byte[] UrlHash { get; init; }
    public required string Verdict { get; set; }
    public string[] ThreatTypes { get; set; } = [];
    public required DateTimeOffset ExpiresAt { get; set; }
    public required DateTimeOffset CreatedAt { get; init; }
}
