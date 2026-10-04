using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Features.Jobs;
using HockeyIndex.Api.Infrastructure.Auth;
using HockeyIndex.Api.Infrastructure.Persistence;
using HockeyIndex.Api.Infrastructure.Text;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Npgsql;

namespace HockeyIndex.Api.Features.Admin;

/// <summary>Phone and domain blocklist (AC-TS-5). Adding a domain runs <see cref="DomainBlocklistSweepJob"/> right after commit.</summary>
public sealed class ManageBlocklist(AppDbContext db, AuditRecorder audit, DomainBlocklistSweepJob sweep, IClock clock)
{
    public async Task<IReadOnlyList<BlocklistEntryResponse>> ListAsync(CancellationToken cancellationToken) =>
    [
        .. (await db.Blocklist.AsNoTracking().OrderByDescending(entry => entry.CreatedAt).ToListAsync(cancellationToken))
            .Select(BlocklistEntryResponse.From),
    ];

    public async Task<IResult> AddAsync(BlocklistAddRequest request, string reason, Guid adminId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        BlocklistKind kind;
        string value;
        switch (request.Kind)
        {
            case "phone":
                var phone = PhoneNormalizer.Normalize(request.Value);
                if (phone.Phone is null)
                {
                    return AdminProblems.Invalid("invalid_blocklist_value", "Enter a valid phone number.");
                }

                kind = BlocklistKind.Phone;
                value = phone.Phone.E164;
                break;
            case "domain":
                if (NormalizeDomain(request.Value) is not { } domain)
                {
                    return AdminProblems.Invalid("invalid_blocklist_value", "Enter a domain name such as example.com.");
                }

                kind = BlocklistKind.Domain;
                value = domain;
                break;
            default:
                return AdminProblems.Invalid("invalid_blocklist_kind", "Kind must be phone or domain.");
        }

        var entry = new BlocklistEntry
        {
            Kind = kind,
            Value = value,
            Reason = reason,
            CreatedBy = adminId,
            CreatedAt = clock.GetCurrentInstant().ToDateTimeOffset(),
        };

        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            db.Blocklist.Add(entry);
            audit.Record(adminId, "blocklist.add", AuditTargets.Blocklist, entry.Id.ToString(), reason, new { Kind = request.Kind, Value = value });
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                return AdminProblems.BlocklistDuplicate();
            }

            await transaction.CommitAsync(cancellationToken);
        }

        if (kind == BlocklistKind.Domain)
        {
            await sweep.RunOnceAsync(cancellationToken);
        }

        return TypedResults.Created($"/v1/admin/blocklist/{entry.Id}", BlocklistEntryResponse.From(entry));
    }

    public async Task<IResult> RemoveAsync(Guid id, string reason, Guid adminId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var entry = await db.Blocklist.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (entry is null)
        {
            return AdminProblems.BlocklistNotFound();
        }

        db.Blocklist.Remove(entry);
        audit.Record(
            adminId, "blocklist.remove", AuditTargets.Blocklist, id.ToString(), reason,
            new { Kind = entry.Kind == BlocklistKind.Phone ? "phone" : "domain", entry.Value });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>Lowercase punycode host with at least one dot; accepts a bare host or a URL (its path is ignored).</summary>
    private static string? NormalizeDomain(string? input)
    {
        var trimmed = input?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > 253)
        {
            return null;
        }

        var candidate = trimmed.Contains("://", StringComparison.Ordinal) ? trimmed : $"https://{trimmed}";
        if (!UrlExtractor.TryNormalize(candidate, out var url, out _) || url.HostNameType != UriHostNameType.Dns)
        {
            return null;
        }

        var host = UrlExtractor.ToAsciiHost(url.Host);
        return host.Contains('.', StringComparison.Ordinal) ? host : null;
    }
}
