using HockeyIndex.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Host = HockeyIndex.Api.Domain.Host;

namespace HockeyIndex.Api.Infrastructure.Persistence.Configurations;

/// <remarks>
/// <c>ix_events_time_active</c> is an expression gist index on <c>tstzrange(starts_at, ends_at, '[)')</c>; EF cannot model it,
/// so migration 0005 creates it with raw SQL.
/// </remarks>
internal sealed class EventConfiguration : IEntityTypeConfiguration<HockeyEvent>
{
    public const string PublicStatusFilter = "status IN ('published','cancelled')";

    public void Configure(EntityTypeBuilder<HockeyEvent> builder)
    {
        builder.ToTable("events", table =>
        {
            table.HasCheckConstraint("ck_events_public_id", $"public_id ~ '{PublicId.Pattern}'");
            table.HasCheckConstraint("ck_events_type", $"type IN ({SnakeCaseText.SqlList<EventType>()})");
            table.HasCheckConstraint("ck_events_time_order", "ends_at > starts_at");
            table.HasCheckConstraint("ck_events_local_time_order", "ends_local > starts_local");
            table.HasCheckConstraint("ck_events_schedule_text", "type <> 'scrimmage' OR schedule_text IS NULL");
            table.HasCheckConstraint(
                "ck_events_skill_range",
                $"skill_range <@ int4range({HockeyEvent.MinSkill},{HockeyEvent.MaxSkill},'[]') AND NOT isempty(skill_range) AND NOT lower_inf(skill_range) AND NOT upper_inf(skill_range)");
            table.HasCheckConstraint("ck_events_fee_cents", $"fee_cents IS NULL OR fee_cents BETWEEN 0 AND {HockeyEvent.MaxFeeCents}");
            table.HasCheckConstraint("ck_events_currency", "currency IN ('CAD','USD')");
            table.HasCheckConstraint("ck_events_status", $"status IN ({SnakeCaseText.SqlList<EventStatus>()})");
            table.HasCheckConstraint("ck_events_publish_requested", "publish_requested_at IS NULL OR status = 'draft'");
            table.HasCheckConstraint("ck_events_pending_join_not_draft", "pending_join_instructions IS NULL OR status <> 'draft'");
            table.HasCheckConstraint("ck_events_hidden_reason", $"hidden_reason IS NULL OR hidden_reason IN ({SnakeCaseText.SqlList<HiddenReason>()})");
            table.HasCheckConstraint("ck_events_hidden_reason_required", "status <> 'hidden' OR hidden_reason IS NOT NULL");
            table.HasCheckConstraint("ck_events_hidden_reason_scope", "status IN ('hidden','archived') OR hidden_reason IS NULL");
            table.HasCheckConstraint(
                "ck_events_hidden_from_status", "hidden_from_status IS NULL OR hidden_from_status IN ('published','cancelled','archived')");
            table.HasCheckConstraint("ck_events_hidden_from_status_required", "status <> 'hidden' OR hidden_from_status IS NOT NULL");
            table.HasCheckConstraint("ck_events_archived_at", "status <> 'archived' OR archived_at IS NOT NULL");
            table.HasCheckConstraint("ck_events_notice", $"notice IS NULL OR notice IN ({SnakeCaseText.SqlList<EventNotice>()})");
        });

        builder.HasKey(evt => evt.Id);
        builder.Property(evt => evt.Id).ValueGeneratedNever();
        builder.Property(evt => evt.PublicId).HasMaxLength(PublicId.Length).IsFixedLength();
        builder.Property(evt => evt.RinkLabel).HasMaxLength(HockeyEvent.RinkLabelMaxLength);
        builder.Property(evt => evt.Type).HasConversion<SnakeCaseEnumConverter<EventType>>();
        builder.Property(evt => evt.Title).HasMaxLength(HockeyEvent.TitleMaxLength);
        builder.Property(evt => evt.Description).HasMaxLength(HockeyEvent.DescriptionMaxLength);
        builder.Property(evt => evt.StartsLocal).HasColumnType("timestamp without time zone");
        builder.Property(evt => evt.EndsLocal).HasColumnType("timestamp without time zone");
        builder.Property(evt => evt.ScheduleText).HasMaxLength(HockeyEvent.ScheduleTextMaxLength);
        builder.Property(evt => evt.SkillRange).HasColumnType("int4range");
        builder.Property(evt => evt.Currency).HasMaxLength(3).IsFixedLength();
        builder.Property(evt => evt.JoinInstructions).HasMaxLength(HockeyEvent.JoinInstructionsMaxLength);
        builder.Property(evt => evt.PendingJoinInstructions).HasMaxLength(HockeyEvent.JoinInstructionsMaxLength);
        builder.Property(evt => evt.Status).HasConversion<SnakeCaseEnumConverter<EventStatus>>();
        builder.Property(evt => evt.HiddenReason).HasConversion<SnakeCaseEnumConverter<HiddenReason>>();
        builder.Property(evt => evt.HiddenFromStatus).HasConversion<SnakeCaseEnumConverter<EventStatus>>();
        builder.Property(evt => evt.Notice).HasConversion<SnakeCaseEnumConverter<EventNotice>>();
        builder.Property(evt => evt.Version).IsRowVersion();
        builder.Ignore(evt => evt.Lifecycle);

        builder.HasIndex(evt => evt.PublicId).IsUnique().HasDatabaseName("ux_events_public_id");
        builder.HasIndex(evt => evt.SkillRange).HasMethod("gist").HasFilter(PublicStatusFilter).HasDatabaseName("ix_events_skill");
        builder.HasIndex(evt => new { evt.Status, evt.EndsAt }).HasDatabaseName("ix_events_status_ends");
        builder.HasIndex(evt => new { evt.HostId, evt.Status, evt.EndsAt }).HasDatabaseName("ix_events_host_status");
        builder.HasIndex(evt => evt.PublishRequestedAt)
            .HasFilter("publish_requested_at IS NOT NULL")
            .HasDatabaseName("ix_events_publish_requested");
        builder.HasIndex(evt => evt.Id, "ix_events_pending_ji")
            .HasFilter("pending_join_instructions IS NOT NULL")
            .HasDatabaseName("ix_events_pending_ji");
        builder.HasIndex(evt => evt.VenueId).HasDatabaseName("ix_events_venue_id");

        builder.HasOne<Host>().WithMany().HasForeignKey(evt => evt.HostId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Venue>().WithMany().HasForeignKey(evt => evt.VenueId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EventStatusChangeConfiguration : IEntityTypeConfiguration<EventStatusChange>
{
    public void Configure(EntityTypeBuilder<EventStatusChange> builder)
    {
        var statuses = SnakeCaseText.SqlList<EventStatus>();
        builder.ToTable("event_status_changes", table =>
        {
            table.HasCheckConstraint("ck_event_status_changes_from_status", $"from_status IN ({statuses})");
            table.HasCheckConstraint("ck_event_status_changes_to_status", $"to_status IN ({statuses})");
        });
        builder.HasKey(change => change.Id);
        builder.Property(change => change.Id).ValueGeneratedNever();
        builder.Property(change => change.FromStatus).HasConversion<SnakeCaseEnumConverter<EventStatus>>();
        builder.Property(change => change.ToStatus).HasConversion<SnakeCaseEnumConverter<EventStatus>>();
        builder.Property(change => change.Reason).HasMaxLength(200);

        builder.HasIndex(change => new { change.HostId, change.ToStatus, change.At }).HasDatabaseName("ix_event_status_changes_host_to_at");
        builder.HasIndex(change => change.EventId).HasDatabaseName("ix_event_status_changes_event_id");
        builder.HasOne<HockeyEvent>().WithMany().HasForeignKey(change => change.EventId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class LinkScanConfiguration : IEntityTypeConfiguration<LinkScan>
{
    public void Configure(EntityTypeBuilder<LinkScan> builder)
    {
        builder.ToTable("link_scans", table =>
        {
            table.HasCheckConstraint("ck_link_scans_verdict", "verdict IN ('safe','malicious','unknown')");
            table.HasCheckConstraint("ck_link_scans_provider", "provider IN ('webrisk','blocklist','none')");
        });
        builder.HasKey(scan => scan.Id);
        builder.Property(scan => scan.Id).ValueGeneratedNever();
        builder.Property(scan => scan.Url).HasMaxLength(2048);
        builder.Property(scan => scan.FinalUrl).HasMaxLength(2048);
        builder.Property(scan => scan.FinalHost).HasMaxLength(255);
        builder.Property(scan => scan.Verdict).HasMaxLength(16);
        builder.Property(scan => scan.Provider).HasMaxLength(16);

        builder.HasIndex(scan => new { scan.EventId, scan.CheckedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_link_scans_event_checked");
        builder.HasIndex(scan => scan.FinalHost).HasDatabaseName("ix_link_scans_final_host");
        builder.HasOne<HockeyEvent>().WithMany().HasForeignKey(scan => scan.EventId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class JobRunConfiguration : IEntityTypeConfiguration<JobRun>
{
    public void Configure(EntityTypeBuilder<JobRun> builder)
    {
        builder.ToTable("job_runs");
        builder.HasKey(run => run.JobName);
        builder.Property(run => run.JobName).HasMaxLength(JobRun.JobNameMaxLength);
        builder.Property(run => run.Metadata).HasColumnType("jsonb");
    }
}
