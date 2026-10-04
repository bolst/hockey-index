using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Infrastructure.Observability;
using HockeyIndex.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NodaTime;
using Npgsql;

namespace HockeyIndex.Api.Infrastructure.RateLimiting;

public sealed record ProviderCaps(int Global, int? PerHost);

/// <summary>
/// Daily paid-call budget per provider. Charges the per-host row (when a host is known and the provider has a per-host cap)
/// and the global nil-UUID row in one transaction; a refused call is rolled back so it is never counted.
/// Caps: configuration <c>Providers:Caps:{provider}:Global|PerHost</c>, else the defaults below.
/// </summary>
public sealed class ProviderBudget(AppDbContext db, IClock clock, IConfiguration configuration, HockeyIndexMetrics metrics)
{
    private static readonly Dictionary<string, ProviderCaps> DefaultCaps = new(StringComparer.Ordinal)
    {
        [ProviderNames.MapboxTemp] = new(3000, 200),
        [ProviderNames.MapboxPerm] = new(200, 20),
        [ProviderNames.WebRisk] = new(5000, 200),
        [ProviderNames.TwilioVerify] = new(500, null),
        [ProviderNames.TwilioLookup] = new(500, null),
    };

    private const string IncrementSql = """
        INSERT INTO provider_usage (provider, host_id, day, calls, cap)
        VALUES (@provider, @host_id, @day, 1, @cap)
        ON CONFLICT (provider, host_id, day) DO UPDATE
        SET calls = provider_usage.calls + 1, cap = EXCLUDED.cap
        RETURNING calls
        """;

    public async Task<bool> TryConsumeAsync(string provider, Guid? hostId, CancellationToken cancellationToken)
    {
        var caps = GetCaps(provider);
        var day = clock.GetCurrentInstant().InUtc().Date.ToDateOnly();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        if (hostId is { } host && host != ProviderUsage.GlobalHostId && caps.PerHost is { } perHostCap)
        {
            var hostCalls = await IncrementAsync(transaction, provider, host, day, perHostCap, cancellationToken);
            if (hostCalls > perHostCap)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
        }

        var globalCalls = await IncrementAsync(transaction, provider, ProviderUsage.GlobalHostId, day, caps.Global, cancellationToken);
        if (globalCalls > caps.Global)
        {
            await transaction.RollbackAsync(cancellationToken);
            metrics.SetProviderBudget(provider, caps.Global, caps.Global);
            return false;
        }

        await transaction.CommitAsync(cancellationToken);
        metrics.RecordProviderCall(provider, MapboxMode(provider));
        metrics.SetProviderBudget(provider, globalCalls, caps.Global);
        return true;
    }

    private static string? MapboxMode(string provider) => provider switch
    {
        ProviderNames.MapboxTemp => "temporary",
        ProviderNames.MapboxPerm => "permanent",
        _ => null,
    };

    public ProviderCaps GetCaps(string provider)
    {
        if (!DefaultCaps.TryGetValue(provider, out var defaults))
        {
            throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown provider.");
        }

        var section = configuration.GetSection($"Providers:Caps:{provider}");
        return new ProviderCaps(
            section.GetValue<int?>("Global") ?? defaults.Global,
            section.GetValue<int?>("PerHost") ?? defaults.PerHost);
    }

    private async Task<int> IncrementAsync(
        IDbContextTransaction transaction, string provider, Guid hostId, DateOnly day, int cap, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var command = new NpgsqlCommand(IncrementSql, connection, (NpgsqlTransaction)transaction.GetDbTransaction());
        command.Parameters.AddWithValue("provider", provider);
        command.Parameters.AddWithValue("host_id", hostId);
        command.Parameters.AddWithValue("day", day);
        command.Parameters.AddWithValue("cap", cap);
        return (int)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}
