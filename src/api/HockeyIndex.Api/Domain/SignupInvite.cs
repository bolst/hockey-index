namespace HockeyIndex.Api.Domain;

/// <summary>Required for unknown phones when <c>Signup:Mode = invite_only</c>.</summary>
public sealed class SignupInvite
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required string PhoneE164 { get; init; }
    public Guid? CreatedBy { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? UsedAt { get; set; }
}
