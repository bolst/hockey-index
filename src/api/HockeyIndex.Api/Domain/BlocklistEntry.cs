namespace HockeyIndex.Api.Domain;

public enum BlocklistKind
{
    Phone,
    Domain,
}

public sealed class BlocklistEntry
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required BlocklistKind Kind { get; init; }
    public required string Value { get; init; }
    public required string Reason { get; init; }
    public Guid? CreatedBy { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
