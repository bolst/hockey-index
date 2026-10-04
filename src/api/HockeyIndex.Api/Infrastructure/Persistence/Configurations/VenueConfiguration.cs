using HockeyIndex.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Host = HockeyIndex.Api.Domain.Host;

namespace HockeyIndex.Api.Infrastructure.Persistence.Configurations;

internal sealed class VenueConfiguration : IEntityTypeConfiguration<Venue>
{
    public void Configure(EntityTypeBuilder<Venue> builder)
    {
        builder.ToTable("venues", table =>
        {
            table.HasCheckConstraint("ck_venues_country", "country IN ('US','CA')");
            table.HasCheckConstraint("ck_venues_provider", "provider IN ('mapbox','geocodio','esri','osm','manual')");
            table.HasCheckConstraint("ck_venues_public_id", $"public_id ~ '{PublicId.Pattern}'");
        });
        builder.HasKey(venue => venue.Id);
        builder.Property(venue => venue.Id).ValueGeneratedNever();
        builder.Property(venue => venue.PublicId).HasMaxLength(PublicId.Length).IsFixedLength();
        builder.Property(venue => venue.Name).HasMaxLength(Venue.NameMaxLength);
        builder.Property(venue => venue.NameNormalized).HasMaxLength(Venue.NameMaxLength);
        builder.Property(venue => venue.AddressLine).HasMaxLength(Venue.AddressLineMaxLength);
        builder.Property(venue => venue.City).HasMaxLength(Venue.CityMaxLength);
        builder.Property(venue => venue.Region).HasMaxLength(2).IsFixedLength();
        builder.Property(venue => venue.Country).HasMaxLength(2).IsFixedLength();
        builder.Property(venue => venue.Geo).HasColumnType("geography(Point,4326)");
        builder.Property(venue => venue.TimeZone).HasMaxLength(Venue.TimeZoneMaxLength);
        builder.Property(venue => venue.Provider).HasMaxLength(16);
        builder.Property(venue => venue.ProviderPlaceId).HasMaxLength(Venue.ProviderPlaceIdMaxLength);

        builder.HasIndex(venue => venue.PublicId).IsUnique().HasDatabaseName("ux_venues_public_id");
        builder.HasIndex(venue => new { venue.Provider, venue.ProviderPlaceId })
            .IsUnique()
            .HasFilter("provider_place_id IS NOT NULL")
            .HasDatabaseName("ux_venues_provider_place");
        builder.HasIndex(venue => venue.Geo).HasMethod("gist").HasDatabaseName("ix_venues_geo");
        builder.HasIndex(venue => venue.NameNormalized)
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops")
            .HasDatabaseName("ix_venues_name_trgm");

        builder.HasOne<Venue>().WithMany().HasForeignKey(venue => venue.MergedIntoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Host>().WithMany().HasForeignKey(venue => venue.CreatedBy).OnDelete(DeleteBehavior.SetNull);
    }
}
