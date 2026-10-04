using System.Net;
using System.Net.Http.Json;
using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Infrastructure.Persistence;
using HockeyIndex.Api.Integrations.Fakes;
using HockeyIndex.Api.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static HockeyIndex.Api.Tests.Integration.Infrastructure.AuthTestClient;
using Host = HockeyIndex.Api.Domain.Host;

namespace HockeyIndex.Api.Tests.Integration;

public sealed class AuthSessionTests(PostgisFixture postgis) : IntegrationTest(postgis)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Credentialed_request_without_requested_with_header_is_rejected()
    {
        using var client = Factory.CreateHostClient(withRequestedWith: false);

        using var start = await client.StartPhoneAsync(Phone(1));
        using var me = await client.GetMeAsync();

        Assert.Equal(HttpStatusCode.Forbidden, start.StatusCode);
        Assert.Equal("csrf_rejected", await ProblemCodeAsync(start));
        Assert.Equal(HttpStatusCode.Forbidden, me.StatusCode);
        Assert.Empty(Factory.Services.GetRequiredService<FakeSmsVerifier>().StartedNumbers);
    }

    [Fact]
    public async Task Cross_origin_unsafe_request_is_rejected()
    {
        using var client = Factory.CreateHostClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/phone/start")
        {
            Content = JsonContent.Create(new { phone = Phone(1), turnstileToken = PassingTurnstileToken }),
        };
        request.Headers.Add("Origin", "https://evil.example");

        using var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(Factory.Services.GetRequiredService<FakeSmsVerifier>().StartedNumbers);
    }

    [Fact]
    public async Task Me_without_session_is_401_not_a_redirect()
    {
        using var client = Factory.CreateHostClient();

        using var response = await client.GetMeAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Me_returns_the_signed_in_host()
    {
        using var client = Factory.CreateHostClient();
        var hostId = await client.SignInWithPhoneAsync(Phone(1));

        using var response = await client.GetMeAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await ReadJsonAsync(response);
        Assert.Equal(hostId, me.GetProperty("id").GetGuid());
        Assert.Equal(Phone(1), me.GetProperty("phone").GetString());
    }

    [Fact]
    public async Task Security_stamp_change_invalidates_the_session_within_one_minute()
    {
        using var client = Factory.CreateHostClient();
        var hostId = await client.SignInWithPhoneAsync(Phone(1));

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<Host>>();
            var host = await users.FindByIdAsync(hostId.ToString());
            Assert.True((await users.UpdateSecurityStampAsync(host!)).Succeeded);
        }

        Factory.Clock.AdvanceSeconds(30);
        using (var beforeInterval = await client.GetMeAsync())
        {
            Assert.Equal(HttpStatusCode.OK, beforeInterval.StatusCode);
        }

        Factory.Clock.AdvanceSeconds(31);
        using var afterInterval = await client.GetMeAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, afterInterval.StatusCode);
    }

    [Fact]
    public async Task Unchanged_session_survives_stamp_validation()
    {
        using var client = Factory.CreateHostClient();
        await client.SignInWithPhoneAsync(Phone(1));

        Factory.Clock.AdvanceMinutes(5);
        using var response = await client.GetMeAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ban_ends_the_live_session_within_one_minute()
    {
        using var client = Factory.CreateHostClient();
        var hostId = await client.SignInWithPhoneAsync(Phone(1));

        await using (var db = Postgis.CreateDbContext())
        {
            await db.Users.Where(host => host.Id == hostId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(host => host.Status, HostStatus.Banned), Ct);
        }

        Factory.Clock.AdvanceSeconds(61);
        using var response = await client.GetMeAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Sign_out_clears_the_session()
    {
        using var client = Factory.CreateHostClient();
        await client.SignInWithPhoneAsync(Phone(1));

        using var signOut = await client.PostAsync(new Uri("/v1/auth/signout", UriKind.Relative), null, Ct);
        using var me = await client.GetMeAsync();

        Assert.Equal(HttpStatusCode.NoContent, signOut.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Runtime_model_matches_the_migrations()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.False(db.Database.HasPendingModelChanges());
    }
}
