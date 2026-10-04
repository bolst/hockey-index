namespace HockeyIndex.Api.Infrastructure.Persistence.Locks;

/// <summary>
/// First key of two-key <c>pg_advisory_xact_lock(class, key)</c> calls. Classes 1–6 belong to <c>OtpLimiter</c>
/// and 7 to venue creation. New lock users MUST take an unused class and add it here.
/// </summary>
public static class AdvisoryLockClasses
{
    /// <summary>Key: <c>hashtext(job_name)</c>. Taken with <c>pg_try_advisory_xact_lock</c> by each job batch.</summary>
    public const int Jobs = 8;

    /// <summary>Key: <c>hashtext(host_id::text)</c>. Serializes the host's publish caps (with <see cref="HostLock"/>).</summary>
    public const int HostPublish = 9;

    /// <summary>Key: <c>hashtext(reporter_hash hex)</c>. Serializes one reporter's <c>report</c> limit check and insert.</summary>
    public const int Reports = 10;
}
