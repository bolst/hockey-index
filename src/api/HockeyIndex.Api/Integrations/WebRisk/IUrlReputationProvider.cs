namespace HockeyIndex.Api.Integrations.WebRisk;

public enum UrlReputationStatus
{
    Safe,
    Malicious,

    /// <summary>The provider gave no answer; the URL MUST be treated as unscanned.</summary>
    Failed,
}

/// <param name="ExpiresAt">For <see cref="UrlReputationStatus.Malicious"/>: the provider's cache expiry, when given.</param>
/// <param name="IsProviderFault">True when the failure points at the provider (5xx, timeout, auth, quota) and should open the breaker.</param>
public sealed record UrlReputation(
    UrlReputationStatus Status,
    IReadOnlyList<string> ThreatTypes,
    DateTimeOffset? ExpiresAt = null,
    bool IsProviderFault = false)
{
    public static UrlReputation Safe() => new(UrlReputationStatus.Safe, []);

    public static UrlReputation Malicious(IReadOnlyList<string> threatTypes, DateTimeOffset? expiresAt) =>
        new(UrlReputationStatus.Malicious, threatTypes, expiresAt);

    public static UrlReputation Failed(bool isProviderFault) => new(UrlReputationStatus.Failed, [], IsProviderFault: isProviderFault);
}

/// <summary>Looks up one canonical URL. Implementations MUST NOT throw for provider errors; they return <see cref="UrlReputation.Failed"/>.</summary>
public interface IUrlReputationProvider
{
    Task<UrlReputation> LookupAsync(Uri url, CancellationToken cancellationToken);
}
