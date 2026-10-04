namespace HockeyIndex.Api.Domain;

public sealed class JobRun
{
    public const int JobNameMaxLength = 64;

    public required string JobName { get; init; }
    public DateTimeOffset? LastStartedAt { get; set; }
    public DateTimeOffset? LastSucceededAt { get; set; }
    public string? LastError { get; set; }
    public long? RowsAffected { get; set; }

    /// <summary>JSON object, e.g. <c>{"tzdb":"2025b"}</c>.</summary>
    public string? Metadata { get; set; }
}
