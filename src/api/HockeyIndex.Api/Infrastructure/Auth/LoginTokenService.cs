using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace HockeyIndex.Api.Infrastructure.Auth;

/// <param name="Code">Six-digit code for typing.</param>
/// <param name="LinkToken">Base64url magic-link token; only its SHA-256 hash is stored.</param>
public sealed record IssuedLoginToken(Guid Id, string Code, string LinkToken);

/// <summary>
/// Single-use email codes and magic-link tokens. Codes live 10 minutes and allow 5 attempts; links live 15 minutes.
/// Issuing a token retires the host's earlier unconsumed tokens for the same purpose.
/// </summary>
public sealed class LoginTokenService(AppDbContext db, SecretHasher hasher, IClock clock)
{
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromMinutes(15);

    public async Task<IssuedLoginToken> IssueAsync(string purpose, Guid hostId, string email, CancellationToken cancellationToken)
    {
        var now = Now();
        await db.LoginTokens
            .Where(token => token.HostId == hostId && token.Purpose == purpose && token.ConsumedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.ConsumedAt, now), cancellationToken);

        var id = Guid.CreateVersion7();
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var linkToken = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        db.LoginTokens.Add(new LoginToken
        {
            Id = id,
            Purpose = purpose,
            HostId = hostId,
            Email = email,
            CodeHash = HashCode(id, code),
            LinkHash = HashLink(linkToken),
            CodeExpiresAt = now + CodeLifetime,
            ExpiresAt = now + LinkLifetime,
            CreatedAt = now,
        });
        await db.SaveChangesAsync(cancellationToken);
        return new IssuedLoginToken(id, code, linkToken);
    }

    /// <summary>Checks a typed code against one token; each call spends an attempt, and a match consumes the token.</summary>
    public async Task<LoginToken?> RedeemCodeAsync(Guid tokenId, IReadOnlyCollection<string> purposes, string code, CancellationToken cancellationToken)
    {
        var now = Now();
        var attempted = await db.LoginTokens
            .Where(token => token.Id == tokenId && purposes.Contains(token.Purpose) && token.ConsumedAt == null
                && token.CodeExpiresAt > now && token.Attempts < LoginToken.MaxAttempts)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.Attempts, token => (short)(token.Attempts + 1)), cancellationToken);
        if (attempted == 0)
        {
            return null;
        }

        var candidate = await db.LoginTokens.AsNoTracking().SingleAsync(token => token.Id == tokenId, cancellationToken);
        return CryptographicOperations.FixedTimeEquals(candidate.CodeHash, HashCode(tokenId, code))
            ? await ConsumeAsync(candidate, now, cancellationToken)
            : null;
    }

    /// <summary>Redeems the host's newest live code for <paramref name="purpose"/>.</summary>
    public async Task<LoginToken?> RedeemHostCodeAsync(Guid hostId, string purpose, string code, CancellationToken cancellationToken)
    {
        var tokenId = await db.LoginTokens
            .Where(token => token.HostId == hostId && token.Purpose == purpose && token.ConsumedAt == null)
            .OrderByDescending(token => token.CreatedAt)
            .Select(token => (Guid?)token.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return tokenId is { } id ? await RedeemCodeAsync(id, [purpose], code, cancellationToken) : null;
    }

    /// <param name="hostId">When set, only that host's token can be redeemed.</param>
    public async Task<LoginToken?> RedeemLinkAsync(string purpose, string linkToken, Guid? hostId, CancellationToken cancellationToken)
    {
        var now = Now();
        var linkHash = HashLink(linkToken);
        var candidate = await db.LoginTokens.AsNoTracking()
            .FirstOrDefaultAsync(
                token => token.LinkHash == linkHash && token.Purpose == purpose && token.ConsumedAt == null && token.ExpiresAt > now
                    && (hostId == null || token.HostId == hostId),
                cancellationToken);
        return candidate is null ? null : await ConsumeAsync(candidate, now, cancellationToken);
    }

    private async Task<LoginToken?> ConsumeAsync(LoginToken candidate, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var consumed = await db.LoginTokens
            .Where(token => token.Id == candidate.Id && token.ConsumedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.ConsumedAt, now), cancellationToken);
        return consumed == 1 ? candidate : null;
    }

    private byte[] HashCode(Guid tokenId, string code) => hasher.Hash("login-code", $"{tokenId:N}:{code}");

    private static byte[] HashLink(string linkToken) => SHA256.HashData(Encoding.UTF8.GetBytes(linkToken));

    private DateTimeOffset Now() => clock.GetCurrentInstant().ToDateTimeOffset();
}
