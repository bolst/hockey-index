using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NodaTime;

namespace HockeyIndex.Api.Infrastructure.RateLimiting;

public sealed record OtpTarget(string Channel, byte[] TargetHash, byte[] IpKeyHash, string? NpaNxx = null);

/// <summary>
/// Durable OTP limits on <c>otp_log</c>. Each check runs in one transaction that first takes transaction-scoped advisory
/// locks in a fixed order (target, IP, NPA-NXX), so concurrent requests for the same key serialize and cannot overshoot.
/// A passing check inserts its own row before commit; callers then update that row's outcome.
/// </summary>
public sealed class OtpLimiter(AppDbContext db, IClock clock, IOptions<OtpOptions> options)
{
    private const int SmsTargetLock = 1;
    private const int SmsIpLock = 2;
    private const int NpaNxxLock = 3;
    private const int EmailTargetLock = 4;
    private const int EmailIpLock = 5;
    private const int VerifyTargetLock = 6;

    /// <returns>The reserved <c>sent</c> row id, or <c>null</c> when a limit refused the send (a <c>limited</c> row is logged).</returns>
    public async Task<Guid?> TryReserveSendAsync(OtpTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        var limits = options.Value;
        var isSms = target.Channel == OtpChannels.Sms;
        var now = Now();
        var since = now.AddHours(-1);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync(isSms ? SmsTargetLock : EmailTargetLock, target.TargetHash, cancellationToken);
        await LockAsync(isSms ? SmsIpLock : EmailIpLock, target.IpKeyHash, cancellationToken);
        if (isSms && target.NpaNxx is { } npaNxx)
        {
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({NpaNxxLock}, hashtext({npaNxx}))", cancellationToken);
        }

        var countedSends = CountedSends(target.Channel, since);
        var withinLimits =
            await countedSends.CountAsync(entry => entry.TargetHash == target.TargetHash, cancellationToken) < (isSms ? limits.SmsPerPhonePerHour : limits.EmailPerAddressPerHour)
            && await countedSends.CountAsync(entry => entry.IpKeyHash == target.IpKeyHash, cancellationToken) < (isSms ? limits.SmsPerIpPerHour : limits.EmailPerIpPerHour)
            && (!isSms || target.NpaNxx is null
                || await countedSends.CountAsync(entry => entry.NpaNxx == target.NpaNxx, cancellationToken) < limits.SmsPerNpaNxxPerHour);

        var entry = NewEntry(target, OtpKinds.Send, withinLimits ? OtpOutcomes.Sent : OtpOutcomes.Limited, now);
        db.OtpLog.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return withinLimits ? entry.Id : null;
    }

    /// <returns>The reserved verify row id (outcome <c>failed</c> until marked), or <c>null</c> when too many checks were made.</returns>
    public async Task<Guid?> TryReserveCheckAsync(OtpTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        var now = Now();
        var since = now.AddMinutes(-10);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync(VerifyTargetLock, target.TargetHash, cancellationToken);
        var checks = await db.OtpLog.CountAsync(
            entry => entry.Channel == target.Channel && entry.Kind == OtpKinds.Verify && entry.TargetHash == target.TargetHash && entry.CreatedAt > since,
            cancellationToken);
        var withinLimit = checks < options.Value.ChecksPerTargetPer10Minutes;

        var entry = NewEntry(target, OtpKinds.Verify, withinLimit ? OtpOutcomes.Failed : OtpOutcomes.Limited, now);
        db.OtpLog.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return withinLimit ? entry.Id : null;
    }

    public async Task LogAsync(OtpTarget target, string kind, string outcome, CancellationToken cancellationToken)
    {
        db.OtpLog.Add(NewEntry(target, kind, outcome, Now()));
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task SetOutcomeAsync(Guid entryId, string outcome, CancellationToken cancellationToken) =>
        db.OtpLog.Where(entry => entry.Id == entryId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(entry => entry.Outcome, outcome), cancellationToken);

    public Task SetLineTypeAsync(Guid entryId, string lineType, CancellationToken cancellationToken) =>
        db.OtpLog.Where(entry => entry.Id == entryId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(entry => entry.LineType, lineType), cancellationToken);

    /// <summary>Line type from a Lookup made for this phone within the cache window, so repeat sends skip the paid call.</summary>
    public Task<string?> FindCachedLineTypeAsync(byte[] phoneHash, CancellationToken cancellationToken)
    {
        var since = Now().AddDays(-options.Value.LineTypeCacheDays);
        return db.OtpLog.AsNoTracking()
            .Where(entry => entry.Channel == OtpChannels.Sms && entry.TargetHash == phoneHash && entry.LineType != null && entry.CreatedAt > since)
            .OrderByDescending(entry => entry.CreatedAt)
            .Select(entry => entry.LineType)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<(int Sends, int Verified)> CountSmsLastHourAsync(CancellationToken cancellationToken)
    {
        var since = Now().AddHours(-1);
        var sends = await db.OtpLog.CountAsync(
            entry => entry.Channel == OtpChannels.Sms && entry.Kind == OtpKinds.Send && entry.Outcome == OtpOutcomes.Sent && entry.CreatedAt > since,
            cancellationToken);
        var verified = await db.OtpLog.CountAsync(
            entry => entry.Channel == OtpChannels.Sms && entry.Kind == OtpKinds.Verify && entry.Outcome == OtpOutcomes.Verified && entry.CreatedAt > since,
            cancellationToken);
        return (sends, verified);
    }

    private IQueryable<OtpLogEntry> CountedSends(string channel, DateTimeOffset since) =>
        db.OtpLog.Where(entry => entry.Channel == channel && entry.Kind == OtpKinds.Send
            && OtpOutcomes.CountedSends.Contains(entry.Outcome) && entry.CreatedAt > since);

    private Task<int> LockAsync(int lockClass, byte[] hash, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({lockClass}, hashtext(encode({hash}, 'hex')))", cancellationToken);

    private static OtpLogEntry NewEntry(OtpTarget target, string kind, string outcome, DateTimeOffset now) => new()
    {
        Channel = target.Channel,
        Kind = kind,
        TargetHash = target.TargetHash,
        IpKeyHash = target.IpKeyHash,
        NpaNxx = target.NpaNxx,
        Outcome = outcome,
        CreatedAt = now,
    };

    private DateTimeOffset Now() => clock.GetCurrentInstant().ToDateTimeOffset();
}
