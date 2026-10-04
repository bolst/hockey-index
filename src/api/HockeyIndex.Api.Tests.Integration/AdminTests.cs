using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Features.Events;
using HockeyIndex.Api.Infrastructure.Auth;
using HockeyIndex.Api.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Npgsql;
using static HockeyIndex.Api.Tests.Integration.Infrastructure.AuthTestClient;
using static HockeyIndex.Api.Tests.Integration.Infrastructure.EventTestSupport;

namespace HockeyIndex.Api.Tests.Integration;

public sealed class AdminTests(PostgisFixture postgis) : IntegrationTest(postgis)
{
    private const string Reason = "Moderation test";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateTimeOffset Now(ApiFactory factory) => factory.Clock.GetCurrentInstant().ToDateTimeOffset();

    [Fact]
    public async Task Admin_routes_require_an_active_admin_a_reason_and_the_csrf_header()
    {
        using var anonymous = Factory.CreateHostClient();
        using var host = Factory.CreateHostClient();
        await host.SignInWithPhoneAsync(Phone(1));
        var (admin, _) = await AdminClientAsync(Factory, 2);

        using var anonymousQueue = await anonymous.GetAsync(new Uri("/v1/admin/queue", UriKind.Relative), Ct);
        using var hostQueue = await host.GetAsync(new Uri("/v1/admin/queue", UriKind.Relative), Ct);
        using var adminQueue = await admin.GetAsync(new Uri("/v1/admin/queue", UriKind.Relative), Ct);
        using var blankReason = await admin.PostAsJsonAsync("/v1/admin/breakers/mapbox", new { action = "open", reason = "  " }, Ct);
        admin.DefaultRequestHeaders.Remove(RequestedWithGuard.HeaderName);
        using var csrf = await admin.GetAsync(new Uri("/v1/admin/queue", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousQueue.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, hostQueue.StatusCode);
        Assert.Equal(HttpStatusCode.OK, adminQueue.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, blankReason.StatusCode);
        Assert.Equal("reason_required", await ProblemCodeAsync(blankReason));
        Assert.Equal(HttpStatusCode.Forbidden, csrf.StatusCode);
        await using var db = Postgis.CreateDbContext();
        Assert.False(await db.AuditLog.AnyAsync(Ct));
        admin.Dispose();
    }

    [Fact]
    public async Task Every_admin_action_writes_exactly_one_audit_row_with_its_reason()
    {
        var (admin, adminId) = await AdminClientAsync(Factory, 1);
        using var host = Factory.CreateHostClient();
        var hostId = await host.SignInWithPhoneAsync(Phone(2));
        await using var db = Postgis.CreateDbContext();
        var venue = await SeedVenueAsync(db, Now(Factory));
        var draft = await SeedDraftAsync(db, hostId, venue, Now(Factory));
        await ForceStatusAsync(db, draft.Id, "published");

        var actions = new (string Action, Func<Task<HttpResponseMessage>> Send, HttpStatusCode Expected)[]
        {
            ("event.hide", () => admin.PostAsJsonAsync($"/v1/admin/events/{draft.PublicId}/hide", new { reason = Reason }, Ct), HttpStatusCode.OK),
            ("event.restore", () => admin.PostAsJsonAsync($"/v1/admin/events/{draft.PublicId}/restore", new { reason = Reason }, Ct), HttpStatusCode.OK),
            ("blocklist.add", () => admin.PostAsJsonAsync("/v1/admin/blocklist", new { kind = "domain", value = "bad.example.com", reason = Reason }, Ct), HttpStatusCode.Created),
            ("invite.create", () => admin.PostAsJsonAsync("/v1/admin/invites", new { phone = Phone(9), reason = Reason }, Ct), HttpStatusCode.Created),
            ("breaker.open", () => admin.PostAsJsonAsync("/v1/admin/breakers/mapbox", new { action = "open", minutes = 30, reason = Reason }, Ct), HttpStatusCode.OK),
            ("breaker.close", () => admin.PostAsJsonAsync("/v1/admin/breakers/mapbox", new { action = "close", reason = Reason }, Ct), HttpStatusCode.OK),
            ("venue.update", () => admin.PutAsJsonAsync($"/v1/admin/venues/{venue.PublicId}", new { name = "Renamed Arena", reason = Reason }, Ct), HttpStatusCode.OK),
            ("host.ban", () => admin.PostAsJsonAsync($"/v1/admin/hosts/{hostId}/ban", new { reason = Reason }, Ct), HttpStatusCode.OK),
            ("host.unban", () => admin.PostAsJsonAsync($"/v1/admin/hosts/{hostId}/unban", new { reason = Reason }, Ct), HttpStatusCode.OK),
        };

        foreach (var (action, send, expected) in actions)
        {
            using var response = await send();
            Assert.True(expected == response.StatusCode, $"{action}: {response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
        }

        var blocklistId = (await db.Blocklist.SingleAsync(Ct)).Id;
        var inviteId = (await db.SignupInvites.SingleAsync(Ct)).Id;
        using (var removeBlock = await DeleteWithReasonAsync(admin, $"/v1/admin/blocklist/{blocklistId}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, removeBlock.StatusCode);
        }

        using (var revokeInvite = await DeleteWithReasonAsync(admin, $"/v1/admin/invites/{inviteId}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, revokeInvite.StatusCode);
        }

        var audit = await db.AuditLog.AsNoTracking().OrderBy(entry => entry.Id).ToListAsync(Ct);
        Assert.Equal(
            [.. actions.Select(action => action.Action), "blocklist.remove", "invite.revoke"],
            audit.Select(entry => entry.Action).ToList());
        Assert.All(audit, entry => Assert.Equal((adminId, Reason), (entry.ActorId, entry.Reason)));

        using var page = await admin.GetAsync(new Uri("/v1/admin/audit?limit=5", UriKind.Relative), Ct);
        var body = await ReadJsonAsync(page);
        Assert.Equal(5, body.GetProperty("items").GetArrayLength());
        Assert.Equal("invite.revoke", body.GetProperty("items")[0].GetProperty("action").GetString());
        Assert.Equal(JsonValueKind.Number, body.GetProperty("nextCursor").ValueKind);
        admin.Dispose();
    }

    [Fact]
    public async Task The_app_role_cannot_update_or_delete_audit_rows_and_the_trigger_stops_the_owner()
    {
        var (admin, _) = await AdminClientAsync(Factory, 1);
        using (var open = await admin.PostAsJsonAsync("/v1/admin/breakers/webrisk", new { action = "open", reason = Reason }, Ct))
        {
            Assert.Equal(HttpStatusCode.OK, open.StatusCode);
        }

        foreach (var sql in new[] { "UPDATE audit_log SET reason = 'rewritten'", "DELETE FROM audit_log", "TRUNCATE audit_log" })
        {
            var denied = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(Postgis.AppConnectionString, sql));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
        }

        var blocked = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(Postgis.MigratorConnectionString, "UPDATE audit_log SET reason = 'rewritten'"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, blocked.SqlState);
        Assert.Contains("append-only", blocked.MessageText, StringComparison.Ordinal);

        await using var db = Postgis.CreateDbContext();
        Assert.Equal(Reason, (await db.AuditLog.SingleAsync(Ct)).Reason);
        admin.Dispose();
    }

    [Fact]
    public async Task Ban_racing_publishes_leaves_no_visible_listing_and_ends_sessions()
    {
        var (admin, _) = await AdminClientAsync(Factory, 1);
        using var host = Factory.CreateHostClient();
        var hostId = await host.SignInWithPhoneAsync(Phone(2));
        await using var db = Postgis.CreateDbContext();
        var venue = await SeedVenueAsync(db, Now(Factory));
        var published = await SeedDraftAsync(db, hostId, venue, Now(Factory));
        await ForceStatusAsync(db, published.Id, "published");
        var archived = await SeedDraftAsync(db, hostId, venue, Now(Factory));
        await ForceStatusAsync(db, archived.Id, "archived", archivedAt: Now(Factory));
        var drafts = new List<HockeyEvent>();
        for (var i = 0; i < 4; i++)
        {
            drafts.Add(await SeedDraftAsync(db, hostId, venue, Now(Factory)));
        }

        var publishes = drafts.Select(draft => host.PostEventActionAsync(draft.PublicId, "publish")).ToList();
        var ban = admin.PostAsJsonAsync($"/v1/admin/hosts/{hostId}/ban", new { reason = Reason }, Ct);
        await Task.WhenAll([.. publishes, ban]);

        using var banResponse = await ban;
        Assert.Equal(HttpStatusCode.OK, banResponse.StatusCode);
        foreach (var publish in publishes)
        {
            using var response = await publish;
            Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        }

        var events = await db.Events.AsNoTracking().Where(evt => evt.HostId == hostId).ToListAsync(Ct);
        Assert.DoesNotContain(events, evt => evt.Status is EventStatus.Published or EventStatus.Cancelled);
        Assert.DoesNotContain(events, evt => evt.Status == EventStatus.Archived && evt.HiddenReason is null);
        Assert.Equal(HiddenReason.HostBanned, events.Single(evt => evt.Id == archived.Id).HiddenReason);
        Assert.All(events.Where(evt => evt.Status == EventStatus.Hidden), evt => Assert.Equal(HiddenReason.HostBanned, evt.HiddenReason));
        var bannedHost = await db.Users.AsNoTracking().SingleAsync(candidate => candidate.Id == hostId, Ct);
        Assert.Equal(HostStatus.Banned, bannedHost.Status);
        Assert.Single(await db.AuditLog.Where(entry => entry.Action == "host.ban").ToListAsync(Ct));

        Factory.Clock.Advance(Duration.FromTimeSpan(AuthSetup.SecurityStampInterval) + Duration.FromSeconds(1));
        using var me = await host.GetMeAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        admin.Dispose();
    }

    [Fact]
    public async Task Restore_over_the_active_cap_succeeds_and_records_cap_override()
    {
        var (admin, _) = await AdminClientAsync(Factory, 1);
        using var host = Factory.CreateHostClient();
        var hostId = await host.SignInWithPhoneAsync(Phone(2));
        await using var db = Postgis.CreateDbContext();
        var venue = await SeedVenueAsync(db, Now(Factory));
        for (var i = 0; i < PublishEvent.MaxActiveListings; i++)
        {
            await ForceStatusAsync(db, (await SeedDraftAsync(db, hostId, venue, Now(Factory))).Id, "published");
        }

        var hidden = await SeedDraftAsync(db, hostId, venue, Now(Factory));
        await ForceStatusAsync(db, hidden.Id, "hidden", hiddenReason: "reports", hiddenFromStatus: "published");

        using var restore = await admin.PostAsJsonAsync($"/v1/admin/events/{hidden.PublicId}/restore", new { reason = Reason }, Ct);

        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        Assert.Equal(EventStatus.Published, (await LoadAsync(db, hidden.Id)).Status);
        var entry = await db.AuditLog.AsNoTracking().SingleAsync(Ct);
        Assert.Equal("event.restore", entry.Action);
        using var metadata = JsonDocument.Parse(entry.Metadata!);
        var capOverride = metadata.RootElement.GetProperty("cap_override");
        Assert.Equal(PublishEvent.MaxActiveListings, capOverride.GetProperty("active_before").GetInt32());
        Assert.Equal(PublishEvent.MaxActiveListings, capOverride.GetProperty("cap").GetInt32());
        admin.Dispose();
    }

    [Fact]
    public async Task Clearing_the_hidden_reason_of_an_archived_event_makes_its_page_public()
    {
        var (admin, _) = await AdminClientAsync(Factory, 1);
        using var host = Factory.CreateHostClient();
        var hostId = await host.SignInWithPhoneAsync(Phone(2));
        await using var db = Postgis.CreateDbContext();
        var venue = await SeedVenueAsync(db, Now(Factory));
        var evt = await SeedDraftAsync(db, hostId, venue, Now(Factory));
        await ForceStatusAsync(db, evt.Id, "archived", hiddenReason: "reports", archivedAt: Now(Factory));
        using var visitor = Factory.CreatePublicClient();

        using (var before = await visitor.GetAsync(new Uri($"/v1/events/{evt.PublicId}", UriKind.Relative), Ct))
        {
            Assert.Equal(HttpStatusCode.NotFound, before.StatusCode);
        }

        using var clear = await admin.PostAsJsonAsync($"/v1/admin/events/{evt.PublicId}/clear-hidden-reason", new { reason = Reason }, Ct);
        using var after = await visitor.GetAsync(new Uri($"/v1/events/{evt.PublicId}", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        var stored = await LoadAsync(db, evt.Id);
        Assert.Equal((EventStatus.Archived, null), (stored.Status, stored.HiddenReason));
        Assert.Equal("event.clear_hidden_reason", (await db.AuditLog.SingleAsync(Ct)).Action);
        admin.Dispose();
    }

    [Fact]
    public async Task Admin_restore_marks_reports_reviewed_so_three_new_reporters_are_needed()
    {
        var (admin, _) = await AdminClientAsync(Factory, 1);
        using var host = Factory.CreateHostClient();
        var hostId = await host.SignInWithPhoneAsync(Phone(2));
        await using var db = Postgis.CreateDbContext();
        var venue = await SeedVenueAsync(db, Now(Factory));
        var evt = await SeedDraftAsync(db, hostId, venue, Now(Factory));
        await ForceStatusAsync(db, evt.Id, "published");
        for (var n = 1; n <= 3; n++)
        {
            using var reporter = Factory.CreatePublicClient($"198.51.100.{n}");
            using var report = await reporter.PostAsJsonAsync($"/v1/events/{evt.PublicId}/reports", new { reason = "spam", turnstileToken = PassingTurnstileToken }, Ct);
            Assert.Equal(HttpStatusCode.NoContent, report.StatusCode);
        }

        using (var queue = await admin.GetAsync(new Uri("/v1/admin/queue", UriKind.Relative), Ct))
        {
            var item = (await ReadJsonAsync(queue))[0];
            Assert.Equal("reports", item.GetProperty("hiddenReason").GetString());
            Assert.Equal(3, item.GetProperty("unreviewedReports").GetInt32());
        }

        using var restore = await admin.PostAsJsonAsync($"/v1/admin/events/{evt.PublicId}/restore", new { reason = Reason }, Ct);
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        using var fourth = Factory.CreatePublicClient("198.51.100.4");
        using var again = await fourth.PostAsJsonAsync($"/v1/events/{evt.PublicId}/reports", new { reason = "spam", turnstileToken = PassingTurnstileToken }, Ct);

        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.Equal(EventStatus.Published, (await LoadAsync(db, evt.Id)).Status);
        Assert.Equal(3, await db.Reports.CountAsync(report => report.ReviewedAt != null, Ct));
        admin.Dispose();
    }

    [Fact]
    public async Task Bootstrap_phones_become_admins_on_sign_in_with_an_audit_row()
    {
        var factory = CreateFactory(new Dictionary<string, string?> { ["Admin:BootstrapPhones:0"] = Phone(5) });
        using var client = factory.CreateHostClient();

        var adminId = await client.SignInWithPhoneAsync(Phone(5));

        using var me = await client.GetMeAsync();
        Assert.True((await ReadJsonAsync(me)).GetProperty("isAdmin").GetBoolean());
        using var queue = await client.GetAsync(new Uri("/v1/admin/queue", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, queue.StatusCode);
        await using var db = Postgis.CreateDbContext();
        var entry = await db.AuditLog.SingleAsync(Ct);
        Assert.Equal(("admin.bootstrap", adminId), (entry.Action, entry.ActorId));
    }

    private async Task<(HttpClient Client, Guid AdminId)> AdminClientAsync(ApiFactory factory, int phone)
    {
        var client = factory.CreateHostClient();
        var adminId = await client.SignInWithPhoneAsync(Phone(phone));
        await using var db = Postgis.CreateDbContext();
        await db.Database.ExecuteSqlAsync($"UPDATE hosts SET is_admin = true WHERE id = {adminId}", Ct);
        return (client, adminId);
    }

    private static Task<HttpResponseMessage> DeleteWithReasonAsync(HttpClient client, string path) =>
        client.SendAsync(
            new HttpRequestMessage(HttpMethod.Delete, new Uri(path, UriKind.Relative)) { Content = JsonContent.Create(new { reason = Reason }) },
            Ct);

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }
}
