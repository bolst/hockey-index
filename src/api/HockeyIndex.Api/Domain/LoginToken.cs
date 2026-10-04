namespace HockeyIndex.Api.Domain;

public static class LoginTokenPurposes
{
    public const string EmailConfirm = "email_confirm";
    public const string EmailLogin = "email_login";
    public const string RecycleCheck = "recycle_check";
}

/// <summary>Single-use email code (HMAC) and magic-link token (SHA-256). Purged after one day.</summary>
public sealed class LoginToken
{
    public const int MaxAttempts = 5;

    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required string Purpose { get; init; }
    public required Guid HostId { get; init; }
    public required string Email { get; init; }
    public required byte[] CodeHash { get; init; }
    public byte[]? LinkHash { get; init; }
    public required DateTimeOffset CodeExpiresAt { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public short Attempts { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public required DateTimeOffset CreatedAt { get; init; }
}
