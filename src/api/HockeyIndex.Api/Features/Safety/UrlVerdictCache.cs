using System.Security.Cryptography;
using System.Text;
using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace HockeyIndex.Api.Features.Safety;

/// <summary>Web Risk verdicts keyed by SHA-256 of the canonical URL. Blocklist verdicts are never cached here.</summary>
public sealed class UrlVerdictCache(AppDbContext db, IClock clock)
{
    public static byte[] HashOf(Uri canonicalUrl)
    {
        ArgumentNullException.ThrowIfNull(canonicalUrl);
        return SHA256.HashData(Encoding.UTF8.GetBytes(canonicalUrl.AbsoluteUri));
    }

    public Task<UrlVerdict?> FindAsync(Uri canonicalUrl, CancellationToken cancellationToken)
    {
        var hash = HashOf(canonicalUrl);
        var now = Now();
        return db.UrlVerdicts.AsNoTracking()
            .SingleOrDefaultAsync(verdict => verdict.UrlHash == hash && verdict.ExpiresAt > now, cancellationToken);
    }

    public async Task StoreAsync(
        Uri canonicalUrl, string verdict, IReadOnlyList<string> threatTypes, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        var hash = HashOf(canonicalUrl);
        var threats = threatTypes.ToArray();
        var expires = expiresAt.ToUniversalTime();
        var now = Now();
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO url_verdicts (url_hash, verdict, threat_types, expires_at, created_at)
            VALUES ({hash}, {verdict}, {threats}, {expires}, {now})
            ON CONFLICT (url_hash) DO UPDATE
            SET verdict = EXCLUDED.verdict,
                threat_types = EXCLUDED.threat_types,
                expires_at = EXCLUDED.expires_at,
                created_at = EXCLUDED.created_at
            """,
            cancellationToken);
    }

    private DateTimeOffset Now() => clock.GetCurrentInstant().ToDateTimeOffset();
}
