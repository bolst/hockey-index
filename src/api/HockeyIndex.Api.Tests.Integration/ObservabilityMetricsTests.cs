using System.Diagnostics.Metrics;
using System.Text.RegularExpressions;
using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Features.Jobs;
using HockeyIndex.Api.Features.Safety;
using HockeyIndex.Api.Infrastructure.Http;
using HockeyIndex.Api.Infrastructure.Observability;
using HockeyIndex.Api.Infrastructure.RateLimiting;
using HockeyIndex.Api.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using NodaTime;

namespace HockeyIndex.Api.Tests.Integration;

public sealed partial class ObservabilityMetricsTests(PostgisFixture postgis) : IntegrationTest(postgis)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Link_scan_emits_verdict_counter_and_duration_histogram_with_bounded_tags()
    {
        using var scans = Collect<long>(HockeyIndexMetrics.LinkScanTotalName);
        using var durations = Collect<double>(HockeyIndexMetrics.LinkScanDurationName);

        await ScanAsync("https://testsafebrowsing.appspot.com/s/phishing.html");
        await ScanAsync("https://example.com/register");
        await ScanAsync("https://down.unavailable.test/");
        await ScanAsync("No links here");

        var verdicts = scans.GetMeasurementSnapshot().Select(m => (string)m.Tags[HockeyIndexMetrics.VerdictTag]!).ToList();
        Assert.Equal(["malicious", "safe", "unknown"], verdicts);
        var recorded = durations.GetMeasurementSnapshot();
        Assert.Equal(3, recorded.Count);
        Assert.All(recorded, m => Assert.True(m.Value >= 0));
        Assert.All(recorded, m => Assert.Empty(m.Tags));
    }

    [Fact]
    public async Task Provider_budget_emits_calls_by_provider_and_remaining_fraction_but_not_for_refused_calls()
    {
        var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["Providers:Caps:webrisk:Global"] = "4",
            ["Providers:Caps:mapbox_temp:Global"] = "1",
        });
        using var calls = Collect<long>(factory, HockeyIndexMetrics.ProviderCallsName);
        using var mapbox = Collect<long>(factory, HockeyIndexMetrics.MapboxCallsName);
        using var remaining = Collect<double>(factory, HockeyIndexMetrics.ProviderBudgetRemainingName);
        using var quota = Collect<double>(factory, HockeyIndexMetrics.WebRiskQuotaUsedName);

        Assert.True(await ConsumeAsync(factory, ProviderNames.WebRisk));
        Assert.True(await ConsumeAsync(factory, ProviderNames.MapboxTemp));
        Assert.False(await ConsumeAsync(factory, ProviderNames.MapboxTemp));

        Assert.Equal(
            [ProviderNames.WebRisk, ProviderNames.MapboxTemp],
            calls.GetMeasurementSnapshot().Select(m => (string)m.Tags[HockeyIndexMetrics.ProviderTag]!));
        Assert.Equal("temporary", Assert.Single(mapbox.GetMeasurementSnapshot()).Tags[HockeyIndexMetrics.ModeTag]);

        remaining.RecordObservableInstruments();
        var byProvider = remaining.GetMeasurementSnapshot()
            .ToDictionary(m => (string)m.Tags[HockeyIndexMetrics.ProviderTag]!, m => m.Value);
        Assert.Equal(0.75, byProvider[ProviderNames.WebRisk]);
        Assert.Equal(0, byProvider[ProviderNames.MapboxTemp]);
        quota.RecordObservableInstruments();
        Assert.Equal(0.25, quota.LastMeasurement!.Value);
    }

    [Fact]
    public async Task Breaker_gauge_follows_open_expiry_and_close()
    {
        using var gauge = Collect<long>(HockeyIndexMetrics.BreakerOpenName);
        var openUntil = Factory.Clock.GetCurrentInstant().Plus(Duration.FromMinutes(10)).ToDateTimeOffset();

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<BreakerService>()
                .OpenAsync(BreakerNames.WebRisk, openUntil, "test", BreakerOpener.Admin, Ct);
        }

        Assert.Equal(1, Observe(gauge, BreakerNames.WebRisk));

        Factory.Clock.Advance(Duration.FromMinutes(11));
        Assert.Equal(0, Observe(gauge, BreakerNames.WebRisk));

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var breakers = scope.ServiceProvider.GetRequiredService<BreakerService>();
            await breakers.OpenAsync(BreakerNames.OtpSms, Factory.Clock.GetCurrentInstant().Plus(Duration.FromHours(1)).ToDateTimeOffset(), "test", BreakerOpener.Auto, Ct);
            Assert.Equal(1, Observe(gauge, BreakerNames.OtpSms));
            await breakers.CloseAsync(BreakerNames.OtpSms, Ct);
        }

        Assert.Equal(0, Observe(gauge, BreakerNames.OtpSms));
    }

    [Fact]
    public async Task Job_run_emits_runs_duration_last_success_and_interval()
    {
        using var runs = Collect<long>(HockeyIndexMetrics.JobRunsName);
        using var durations = Collect<double>(HockeyIndexMetrics.JobDurationName);
        using var lastSuccess = Collect<double>(HockeyIndexMetrics.JobLastSuccessName);
        using var interval = Collect<double>(HockeyIndexMetrics.JobIntervalName);
        var job = Factory.Services.GetRequiredService<ArchiveSweepJob>();

        var result = await job.RunOnceAsync(Ct);

        Assert.Equal(JobRunStatus.Succeeded, result.Status);
        var run = Assert.Single(runs.GetMeasurementSnapshot());
        Assert.Equal(ArchiveSweepJob.JobName, run.Tags[HockeyIndexMetrics.JobTag]);
        Assert.Equal("succeeded", run.Tags[HockeyIndexMetrics.StatusTag]);
        Assert.Equal(ArchiveSweepJob.JobName, Assert.Single(durations.GetMeasurementSnapshot()).Tags[HockeyIndexMetrics.JobTag]);

        lastSuccess.RecordObservableInstruments();
        Assert.Equal(Factory.Clock.GetCurrentInstant().ToUnixTimeSeconds(), lastSuccess.LastMeasurement!.Value);
        interval.RecordObservableInstruments();
        var intervalSample = interval.LastMeasurement!;
        Assert.Equal(ArchiveSweepJob.JobName, intervalSample.Tags[HockeyIndexMetrics.JobTag]);
        Assert.True(intervalSample.Value > 0);
    }

    [Fact]
    public async Task No_metrics_endpoint_is_reachable_on_the_public_or_ops_listener()
    {
        foreach (var path in new[] { "/metrics", "/v1/metrics", "/healthz/metrics" })
        {
            var publicContext = await Factory.SendOnPortAsync(
                ListenerPorts.Public, path, request => request.Headers[ClientIpResolver.HeaderName] = ApiFactory.ClientIp);
            var opsContext = await Factory.SendOnPortAsync(ListenerPorts.Ops, path);

            Assert.Equal(StatusCodes.Status404NotFound, publicContext.Response.StatusCode);
            Assert.Equal(StatusCodes.Status404NotFound, opsContext.Response.StatusCode);
        }
    }

    [Fact]
    public void Every_hi_metric_named_in_alert_rules_is_registered_by_the_meter()
    {
        var registered = new HashSet<string>(StringComparer.Ordinal);
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, _) =>
        {
            if (instrument.Meter.Name == HockeyIndexMetrics.MeterName)
            {
                registered.Add(instrument.Name);
            }
        };
        listener.Start();
        _ = Factory.Services.GetRequiredService<HockeyIndexMetrics>();

        var alerts = File.ReadAllText(FindFromRepositoryRoot("deploy/observability/alerts.yaml"));
        var dashboards = File.ReadAllText(FindFromRepositoryRoot("deploy/observability/dashboards.md"));
        var named = MetricNamePattern().Matches(alerts + dashboards).Select(match => match.Value).ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(named);
        Assert.Empty(named.Except(registered));
    }

    [GeneratedRegex(@"\bhi_[a-z0-9_]+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MetricNamePattern();

    private static string FindFromRepositoryRoot(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(relativePath);
    }

    private static double Observe(MetricCollector<long> gauge, string name)
    {
        gauge.RecordObservableInstruments();
        return gauge.GetMeasurementSnapshot().Last(m => (string?)m.Tags[HockeyIndexMetrics.NameTag] == name).Value;
    }

    private MetricCollector<T> Collect<T>(string instrument)
        where T : struct => Collect<T>(Factory, instrument);

    private static MetricCollector<T> Collect<T>(ApiFactory factory, string instrument)
        where T : struct
    {
        _ = factory.Services.GetRequiredService<HockeyIndexMetrics>();
        return new MetricCollector<T>(factory.Services.GetRequiredService<IMeterFactory>(), HockeyIndexMetrics.MeterName, instrument);
    }

    private async Task ScanAsync(string text)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<LinkScanService>().ScanAsync(text, null, Ct);
    }

    private static async Task<bool> ConsumeAsync(ApiFactory factory, string provider)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ProviderBudget>().TryConsumeAsync(provider, null, Ct);
    }
}
