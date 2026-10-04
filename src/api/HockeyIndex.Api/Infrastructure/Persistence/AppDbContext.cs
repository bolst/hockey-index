using HockeyIndex.Api.Domain;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Host = HockeyIndex.Api.Domain.Host;

namespace HockeyIndex.Api.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityUserContext<Host, Guid>(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<BlocklistEntry> Blocklist => Set<BlocklistEntry>();
    public DbSet<Breaker> Breakers => Set<Breaker>();
    public DbSet<ProviderUsage> ProviderUsage => Set<ProviderUsage>();
    public DbSet<UrlVerdict> UrlVerdicts => Set<UrlVerdict>();
    public DbSet<LoginToken> LoginTokens => Set<LoginToken>();
    public DbSet<SignupInvite> SignupInvites => Set<SignupInvite>();
    public DbSet<OtpLogEntry> OtpLog => Set<OtpLogEntry>();
    public DbSet<Venue> Venues => Set<Venue>();
    public DbSet<HockeyEvent> Events => Set<HockeyEvent>();
    public DbSet<EventStatusChange> EventStatusChanges => Set<EventStatusChange>();
    public DbSet<LinkScan> LinkScans => Set<LinkScan>();
    public DbSet<JobRun> JobRuns => Set<JobRun>();
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.OnModelCreating(builder);

        // postgis is superuser-only and created by deploy/postgres/init; migration 0001 asserts it exists.
        builder
            .HasPostgresExtension("postgis")
            .HasPostgresExtension("pg_trgm")
            .HasPostgresExtension("btree_gist")
            .HasPostgresExtension("citext")
            .HasPostgresExtension("unaccent");

        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
