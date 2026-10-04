using HockeyIndex.Api.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static HockeyIndex.Api.Tests.Integration.Infrastructure.AuthTestClient;
using static HockeyIndex.Api.Tests.Integration.Infrastructure.EventTestSupport;

namespace HockeyIndex.Api.Tests.Integration;

/// <summary>The database rejects invalid rows even when application checks are bypassed.</summary>
public sealed class EventConstraintTests(PostgisFixture postgis) : IntegrationTest(postgis)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("public_id = 'ABCDEFGHIJ'", "ck_events_public_id")]
    [InlineData("public_id = 'abcdefgh01'", "ck_events_public_id")]
    [InlineData("skill_range = int4range(0, 8, '[]')", "ck_events_skill_range")]
    [InlineData("skill_range = int4range(-1, 3, '[]')", "ck_events_skill_range")]
    [InlineData("skill_range = int4range(3, NULL)", "ck_events_skill_range")]
    [InlineData("fee_cents = -1", "ck_events_fee_cents")]
    [InlineData("fee_cents = 100001", "ck_events_fee_cents")]
    [InlineData("currency = 'EUR'", "ck_events_currency")]
    [InlineData("status = 'deleted'", "ck_events_status")]
    [InlineData("status = 'hidden', hidden_from_status = 'published'", "ck_events_hidden_reason_required")]
    [InlineData("status = 'hidden', hidden_reason = 'reports'", "ck_events_hidden_from_status_required")]
    [InlineData("status = 'published', hidden_reason = 'reports'", "ck_events_hidden_reason_scope")]
    [InlineData("status = 'archived'", "ck_events_archived_at")]
    [InlineData("status = 'published', publish_requested_at = now()", "ck_events_publish_requested")]
    [InlineData("pending_join_instructions = 'x'", "ck_events_pending_join_not_draft")]
    [InlineData("schedule_text = 'Tuesdays'", "ck_events_schedule_text")]
    [InlineData("ends_at = starts_at", "ck_events_time_order")]
    [InlineData("ends_local = starts_local", "ck_events_local_time_order")]
    [InlineData("notice = 'bogus'", "ck_events_notice")]
    public async Task Invalid_event_rows_are_rejected(string assignment, string constraint)
    {
        var eventId = await SeedAsync();
        await using var db = Postgis.CreateDbContext();
        var invalidUpdate = "UPDATE events SET " + assignment + " WHERE id = @id";

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync(invalidUpdate, [new NpgsqlParameter("id", eventId)], Ct));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal(constraint, exception.ConstraintName);
    }

    [Fact]
    public async Task Venue_public_ids_must_use_the_base32_alphabet()
    {
        await using var db = Postgis.CreateDbContext();
        var venue = await SeedVenueAsync(db, Factory.Clock.GetCurrentInstant().ToDateTimeOffset());

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlAsync($"UPDATE venues SET public_id = 'abcdefgh0_' WHERE id = {venue.Id}", Ct));

        Assert.Equal("ck_venues_public_id", exception.ConstraintName);
    }

    private async Task<Guid> SeedAsync()
    {
        using var client = Factory.CreateHostClient();
        var hostId = await client.SignInWithPhoneAsync(Phone(1));
        await using var db = Postgis.CreateDbContext();
        var now = Factory.Clock.GetCurrentInstant().ToDateTimeOffset();
        var venue = await SeedVenueAsync(db, now);
        return (await SeedDraftAsync(db, hostId, venue, now)).Id;
    }
}
