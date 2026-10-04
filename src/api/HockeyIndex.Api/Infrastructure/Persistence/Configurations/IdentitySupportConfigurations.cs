using HockeyIndex.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Host = HockeyIndex.Api.Domain.Host;

namespace HockeyIndex.Api.Infrastructure.Persistence.Configurations;

internal sealed class LoginTokenConfiguration : IEntityTypeConfiguration<LoginToken>
{
    public void Configure(EntityTypeBuilder<LoginToken> builder)
    {
        builder.ToTable("login_tokens", table =>
        {
            table.HasCheckConstraint("ck_login_tokens_purpose", "purpose IN ('email_confirm','email_login','recycle_check')");
            table.HasCheckConstraint("ck_login_tokens_attempts", "attempts BETWEEN 0 AND 5");
        });
        builder.HasKey(token => token.Id);
        builder.Property(token => token.Id).ValueGeneratedNever();
        builder.Property(token => token.Email).HasColumnType("citext");
        builder.HasOne<Host>().WithMany().HasForeignKey(token => token.HostId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(token => token.LinkHash).IsUnique().HasFilter("link_hash IS NOT NULL").HasDatabaseName("ux_login_tokens_link_hash");
        builder.HasIndex(token => new { token.HostId, token.Purpose, token.CreatedAt }).HasDatabaseName("ix_login_tokens_host_purpose");
        builder.HasIndex(token => token.CreatedAt).HasDatabaseName("ix_login_tokens_created_at");
    }
}

internal sealed class SignupInviteConfiguration : IEntityTypeConfiguration<SignupInvite>
{
    public void Configure(EntityTypeBuilder<SignupInvite> builder)
    {
        builder.ToTable("signup_invites");
        builder.HasKey(invite => invite.Id);
        builder.Property(invite => invite.Id).ValueGeneratedNever();
        builder.HasIndex(invite => invite.PhoneE164).HasDatabaseName("ix_signup_invites_phone_e164");
    }
}

internal sealed class OtpLogEntryConfiguration : IEntityTypeConfiguration<OtpLogEntry>
{
    public void Configure(EntityTypeBuilder<OtpLogEntry> builder)
    {
        builder.ToTable("otp_log", table =>
        {
            table.HasCheckConstraint("ck_otp_log_channel", "channel IN ('sms','email')");
            table.HasCheckConstraint("ck_otp_log_kind", "kind IN ('send','verify')");
            table.HasCheckConstraint("ck_otp_log_outcome", $"outcome IN ({string.Join(",", OtpOutcomes.All.Select(o => $"'{o}'"))})");
        });
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedNever();
        builder.Property(entry => entry.NpaNxx).HasColumnType("char(6)");
        builder.HasIndex(entry => new { entry.TargetHash, entry.CreatedAt }).HasDatabaseName("ix_otp_log_target_created");
        builder.HasIndex(entry => new { entry.IpKeyHash, entry.CreatedAt }).HasDatabaseName("ix_otp_log_ip_created");
        builder.HasIndex(entry => new { entry.NpaNxx, entry.CreatedAt }).HasDatabaseName("ix_otp_log_npa_nxx_created");
        builder.HasIndex(entry => entry.CreatedAt).HasDatabaseName("ix_otp_log_created_at");
    }
}
