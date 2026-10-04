using System.Net;
using System.Net.Http.Json;
using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Integrations.Email;
using HockeyIndex.Api.Integrations.Fakes;
using HockeyIndex.Api.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static HockeyIndex.Api.Tests.Integration.Infrastructure.AuthTestClient;

namespace HockeyIndex.Api.Tests.Integration;

public sealed class EmailAuthTests(PostgisFixture postgis) : IntegrationTest(postgis)
{
    private const string Email = "jordan@example.com";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private FakeEmailSender Mail => Factory.Services.GetRequiredService<FakeEmailSender>();

    [Fact]
    public async Task Email_can_be_added_confirmed_by_code_and_used_to_sign_in()
    {
        using var phoneClient = Factory.CreateHostClient();
        var hostId = await phoneClient.SignInWithPhoneAsync(Phone(1));

        await AddEmailAsync(phoneClient, Email);
        using (var confirm = await phoneClient.PostAsJsonAsync("/v1/me/email/confirm", new { code = CodeFrom(LastMail()) }, Ct))
        {
            Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
            Assert.Equal(Email, (await ReadJsonAsync(confirm)).GetProperty("email").GetString());
        }

        using (var stillSignedIn = await phoneClient.GetMeAsync())
        {
            Assert.Equal(HttpStatusCode.OK, stillSignedIn.StatusCode);
        }

        using var emailClient = Factory.CreateHostClient();
        var loginId = await StartEmailLoginAsync(emailClient, Email.ToUpperInvariant());
        using var complete = await emailClient.PostAsJsonAsync("/v1/auth/email/complete", new { loginId, code = CodeFrom(LastMail()) }, Ct);
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);

        using var me = await emailClient.GetMeAsync();
        Assert.Equal(hostId, (await ReadJsonAsync(me)).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Magic_links_confirm_and_sign_in_once()
    {
        using var phoneClient = Factory.CreateHostClient();
        await phoneClient.SignInWithPhoneAsync(Phone(1));
        await AddEmailAsync(phoneClient, Email);
        using (var confirm = await phoneClient.PostAsJsonAsync("/v1/me/email/confirm", new { token = LinkTokenFrom(LastMail()) }, Ct))
        {
            Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        }

        using var emailClient = Factory.CreateHostClient();
        await StartEmailLoginAsync(emailClient, Email);
        var token = LinkTokenFrom(LastMail());

        using var first = await emailClient.PostAsJsonAsync("/v1/auth/email/complete", new { token }, Ct);
        using var replay = await emailClient.PostAsJsonAsync("/v1/auth/email/complete", new { token }, Ct);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    [Fact]
    public async Task Unknown_or_unconfirmed_email_gets_202_and_no_mail()
    {
        using var phoneClient = Factory.CreateHostClient();
        await phoneClient.SignInWithPhoneAsync(Phone(1));
        await AddEmailAsync(phoneClient, Email);
        var mailCount = Mail.Sent.Count;

        using var client = Factory.CreateHostClient("198.51.100.20");
        var unconfirmedLoginId = await StartEmailLoginAsync(client, Email);
        var unknownLoginId = await StartEmailLoginAsync(client, "nobody@example.com");

        Assert.NotEqual(Guid.Empty, unconfirmedLoginId);
        Assert.NotEqual(Guid.Empty, unknownLoginId);
        Assert.Equal(mailCount, Mail.Sent.Count);
    }

    [Fact]
    public async Task Email_login_requires_turnstile()
    {
        using var client = Factory.CreateHostClient();

        using var response = await client.PostAsJsonAsync("/v1/auth/email/start", new { email = Email, turnstileToken = "fail" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("turnstile_failed", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Ban_blocks_email_login()
    {
        using var phoneClient = Factory.CreateHostClient();
        var hostId = await ConfirmedHostAsync(phoneClient, Phone(1), Email);
        using var emailClient = Factory.CreateHostClient();
        var loginId = await StartEmailLoginAsync(emailClient, Email);
        var code = CodeFrom(LastMail());

        await using (var db = Postgis.CreateDbContext())
        {
            await db.Users.Where(host => host.Id == hostId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(host => host.Status, HostStatus.Banned), Ct);
        }

        using var complete = await emailClient.PostAsJsonAsync("/v1/auth/email/complete", new { loginId, code }, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, complete.StatusCode);
        Assert.Equal("account_banned", await ProblemCodeAsync(complete));

        var mailCount = Mail.Sent.Count;
        await StartEmailLoginAsync(emailClient, Email);
        Assert.Equal(mailCount, Mail.Sent.Count);
    }

    [Fact]
    public async Task Confirmed_email_belongs_to_one_host()
    {
        using var first = Factory.CreateHostClient();
        await ConfirmedHostAsync(first, Phone(1), Email);

        using var second = Factory.CreateHostClient("198.51.100.30");
        await second.SignInWithPhoneAsync(Phone(2));
        await AddEmailAsync(second, Email);
        using var confirm = await second.PostAsJsonAsync("/v1/me/email/confirm", new { code = CodeFrom(LastMail()) }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, confirm.StatusCode);
        Assert.Equal("email_in_use", await ProblemCodeAsync(confirm));
    }

    [Fact]
    public async Task Removed_email_no_longer_signs_in()
    {
        using var client = Factory.CreateHostClient();
        await ConfirmedHostAsync(client, Phone(1), Email);

        using (var remove = await client.DeleteAsync(new Uri("/v1/me/email", UriKind.Relative), Ct))
        {
            Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
            Assert.Equal(System.Text.Json.JsonValueKind.Null, (await ReadJsonAsync(remove)).GetProperty("email").ValueKind);
        }

        using (var me = await client.GetMeAsync())
        {
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        }

        var mailCount = Mail.Sent.Count;
        using var other = Factory.CreateHostClient("198.51.100.40");
        await StartEmailLoginAsync(other, Email);
        Assert.Equal(mailCount, Mail.Sent.Count);
    }

    [Fact]
    public async Task Login_code_locks_after_five_wrong_attempts()
    {
        using var phoneClient = Factory.CreateHostClient();
        await ConfirmedHostAsync(phoneClient, Phone(1), Email);
        using var client = Factory.CreateHostClient();
        var loginId = await StartEmailLoginAsync(client, Email);
        var code = CodeFrom(LastMail());
        var wrong = code == "000000" ? "111111" : "000000";

        for (var i = 0; i < 5; i++)
        {
            using var attempt = await client.PostAsJsonAsync("/v1/auth/email/complete", new { loginId, code = wrong }, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, attempt.StatusCode);
        }

        using var correct = await client.PostAsJsonAsync("/v1/auth/email/complete", new { loginId, code }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, correct.StatusCode);
    }

    [Fact]
    public async Task Adding_email_requires_a_session()
    {
        using var client = Factory.CreateHostClient();

        using var response = await client.PostAsJsonAsync("/v1/me/email", new { email = Email }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(Mail.Sent);
    }

    private async Task<Guid> ConfirmedHostAsync(HttpClient client, string phone, string email)
    {
        var hostId = await client.SignInWithPhoneAsync(phone);
        await AddEmailAsync(client, email);
        using var confirm = await client.PostAsJsonAsync("/v1/me/email/confirm", new { code = CodeFrom(LastMail()) }, Ct);
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        await using var db = Postgis.CreateDbContext();
        Assert.True((await db.Users.SingleAsync(host => host.Id == hostId, Ct)).EmailConfirmed);
        return hostId;
    }

    private static async Task AddEmailAsync(HttpClient client, string email)
    {
        using var response = await client.PostAsJsonAsync("/v1/me/email", new { email }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    private static async Task<Guid> StartEmailLoginAsync(HttpClient client, string email)
    {
        using var response = await client.PostAsJsonAsync("/v1/auth/email/start", new { email, turnstileToken = PassingTurnstileToken }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("loginId").GetGuid();
    }

    private EmailMessage LastMail() => Mail.Sent.Last();
}
