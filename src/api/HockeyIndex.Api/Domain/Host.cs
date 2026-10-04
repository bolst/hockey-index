using Microsoft.AspNetCore.Identity;

namespace HockeyIndex.Api.Domain;

public enum HostStatus
{
    Active,
    Banned,
}

/// <summary>
/// Phone-rooted account. <see cref="IdentityUser{TKey}.UserName"/> and <see cref="IdentityUser{TKey}.PhoneNumber"/>
/// both hold the E.164 number. <see cref="IdentityUser{TKey}.Email"/> holds only a confirmed address.
/// </summary>
public sealed class Host : IdentityUser<Guid>
{
    public DateTimeOffset PhoneVerifiedAt { get; set; }
    public string? PhoneLineType { get; set; }
    public bool IsVoip { get; set; }
    public DateTimeOffset? EmailVerifiedAt { get; set; }
    public bool IsAdmin { get; set; }
    public HostStatus Status { get; set; } = HostStatus.Active;
    public DateTimeOffset? BannedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
}
