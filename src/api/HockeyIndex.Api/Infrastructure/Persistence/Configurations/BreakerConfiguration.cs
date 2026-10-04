using HockeyIndex.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HockeyIndex.Api.Infrastructure.Persistence.Configurations;

internal sealed class BreakerConfiguration : IEntityTypeConfiguration<Breaker>
{
    public void Configure(EntityTypeBuilder<Breaker> builder)
    {
        builder.ToTable("breakers", table =>
            table.HasCheckConstraint("ck_breakers_opened_by", "opened_by IN ('auto','twilio_webhook','admin')"));
        builder.HasKey(breaker => breaker.Name);
        builder.Property(breaker => breaker.OpenedBy).HasConversion(
            opener => opener.HasValue ? BreakerOpenerValues.ToDatabase(opener.Value) : null,
            value => value == null ? null : BreakerOpenerValues.FromDatabase(value));
    }
}

internal static class BreakerOpenerValues
{
    public static string ToDatabase(BreakerOpener opener) => opener switch
    {
        BreakerOpener.Auto => "auto",
        BreakerOpener.TwilioWebhook => "twilio_webhook",
        BreakerOpener.Admin => "admin",
        _ => throw new ArgumentOutOfRangeException(nameof(opener), opener, null),
    };

    public static BreakerOpener FromDatabase(string value) => value switch
    {
        "auto" => BreakerOpener.Auto,
        "twilio_webhook" => BreakerOpener.TwilioWebhook,
        "admin" => BreakerOpener.Admin,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };
}
