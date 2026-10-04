using HockeyIndex.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HockeyIndex.Api.Infrastructure.Persistence.Configurations;

internal sealed class ReportConfiguration : IEntityTypeConfiguration<Report>
{
    public void Configure(EntityTypeBuilder<Report> builder)
    {
        builder.ToTable("reports", table => table.HasCheckConstraint("ck_reports_reason", $"reason IN ({SnakeCaseText.SqlList<ReportReason>()})"));
        builder.HasKey(report => report.Id);
        builder.Property(report => report.Id).ValueGeneratedNever();
        builder.Property(report => report.Reason).HasConversion<SnakeCaseEnumConverter<ReportReason>>().HasMaxLength(16);
        builder.Property(report => report.Details).HasMaxLength(Report.DetailsMaxLength);

        builder.HasIndex(report => new { report.EventId, report.ReporterHash }).IsUnique().HasDatabaseName("ux_reports_event_reporter");
        builder.HasIndex(report => new { report.ReporterHash, report.CreatedAt }).HasDatabaseName("ix_reports_reporter_created");
        builder.HasOne<HockeyEvent>().WithMany().HasForeignKey(report => report.EventId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.ToTable("audit_log", table => table.HasCheckConstraint("ck_audit_log_reason", "length(btrim(reason)) > 0"));
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).UseIdentityAlwaysColumn();
        builder.Property(entry => entry.Action).HasMaxLength(64);
        builder.Property(entry => entry.TargetType).HasMaxLength(32);
        builder.Property(entry => entry.TargetId).HasMaxLength(64);
        builder.Property(entry => entry.Reason).HasMaxLength(AuditLogEntry.ReasonMaxLength);
        builder.Property(entry => entry.Metadata).HasColumnType("jsonb");

        builder.HasIndex(entry => new { entry.TargetType, entry.TargetId }).HasDatabaseName("ix_audit_log_target");
        builder.HasIndex(entry => entry.CreatedAt).HasDatabaseName("ix_audit_log_created_at");
    }
}
