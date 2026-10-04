using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using NodaTime;

namespace HockeyIndex.Api.Infrastructure.Observability;

/// <summary>
/// Every <c>hi_</c> metric of plan section 9.4 that the API emits. Tags MUST stay bounded (fixed vocabularies only):
/// never ids, URLs, phones or hosts. Pushed over OTLP; the API exposes no scrape endpoint on any port.
/// <c>backup_*</c> series come from the backup container; <c>http_server_request_duration_seconds</c> from ASP.NET Core instrumentation.
/// </summary>
public sealed class HockeyIndexMetrics
{
    public const string MeterName = "HockeyIndex.Api";

    public const string PublicSetCookieStrippedName = "hi_public_set_cookie_stripped_total";
    public const string CfPurgeTotalName = "hi_cf_purge_total";
    public const string EventsPublishedName = "hi_events_published_total";
    public const string EventsAutoHiddenName = "hi_events_auto_hidden_total";
    public const string ReportsName = "hi_reports_total";
    public const string OtpSendsName = "hi_otp_sends_total";
    public const string OtpVerifyName = "hi_otp_verify_total";
    public const string OtpConversionRatioName = "hi_otp_conversion_ratio";
    public const string BreakerOpenName = "hi_breaker_open";
    public const string ProviderCallsName = "hi_provider_calls_total";
    public const string ProviderBudgetRemainingName = "hi_provider_budget_remaining";
    public const string WebRiskQuotaUsedName = "hi_webrisk_quota_used";
    public const string MapboxCallsName = "hi_mapbox_calls_total";
    public const string LinkScanTotalName = "hi_link_scan_total";
    public const string LinkScanDurationName = "hi_link_scan_duration_seconds";
    public const string ScanPendingEventsName = "hi_scan_pending_events";
    public const string SearchDurationName = "hi_search_duration_seconds";
    public const string SearchTruncatedName = "hi_search_truncated_total";
    public const string JobRunsName = "hi_job_runs_total";
    public const string JobDurationName = "hi_job_duration_seconds";
    public const string JobLastSuccessName = "hi_job_last_success_timestamp";
    public const string JobIntervalName = "hi_job_interval_seconds";

    public const string ResultTag = "result";
    public const string OutcomeTag = "outcome";
    public const string VerdictTag = "verdict";
    public const string ProviderTag = "provider";
    public const string NameTag = "name";
    public const string ModeTag = "mode";
    public const string ReasonTag = "reason";
    public const string JobTag = "job_name";
    public const string StatusTag = "status";

    private readonly IClock clock;
    private readonly Counter<long> publicSetCookieStripped;
    private readonly Counter<long> cfPurge;
    private readonly Counter<long> eventsPublished;
    private readonly Counter<long> eventsAutoHidden;
    private readonly Counter<long> reports;
    private readonly Counter<long> otpSends;
    private readonly Counter<long> otpVerify;
    private readonly Counter<long> providerCalls;
    private readonly Counter<long> mapboxCalls;
    private readonly Counter<long> linkScans;
    private readonly Histogram<double> linkScanDuration;
    private readonly Histogram<double> searchDuration;
    private readonly Counter<long> searchTruncated;
    private readonly Counter<long> jobRuns;
    private readonly Histogram<double> jobDuration;

    private readonly ConcurrentDictionary<string, DateTimeOffset> breakerOpenUntil = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, BudgetSample> budgets = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, double> jobLastSuccess = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, double> jobIntervals = new(StringComparer.Ordinal);
    private double otpConversionRatio = double.NaN;
    private long scanPendingEvents = -1;

    public HockeyIndexMetrics(IMeterFactory meterFactory, IClock? clock = null)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        this.clock = clock ?? SystemClock.Instance;
        var meter = meterFactory.Create(MeterName);

        publicSetCookieStripped = meter.CreateCounter<long>(
            PublicSetCookieStrippedName, description: "Set-Cookie headers removed from public responses.");
        cfPurge = meter.CreateCounter<long>(
            CfPurgeTotalName, description: "Cloudflare cache purge calls by final result (success or error after retries).");
        eventsPublished = meter.CreateCounter<long>(EventsPublishedName, description: "Draft-to-published transitions.");
        eventsAutoHidden = meter.CreateCounter<long>(
            EventsAutoHiddenName, description: "Events the system hid without an admin (reports, link scan, domain blocklist).");
        reports = meter.CreateCounter<long>(ReportsName, description: "Event reports accepted.");
        otpSends = meter.CreateCounter<long>(OtpSendsName, description: "SMS sign-in start attempts by outcome.");
        otpVerify = meter.CreateCounter<long>(OtpVerifyName, description: "SMS sign-in code checks by result.");
        providerCalls = meter.CreateCounter<long>(ProviderCallsName, description: "Paid provider calls charged to the daily budget.");
        mapboxCalls = meter.CreateCounter<long>(MapboxCallsName, description: "Mapbox calls charged to the budget by mode (temporary or permanent).");
        linkScans = meter.CreateCounter<long>(LinkScanTotalName, description: "Link scans that contained URLs, by overall verdict.");
        linkScanDuration = meter.CreateHistogram<double>(LinkScanDurationName, unit: "s", description: "Duration of link scans that contained URLs.");
        searchDuration = meter.CreateHistogram<double>(SearchDurationName, unit: "s", description: "Duration of GET /v1/search handling.");
        searchTruncated = meter.CreateCounter<long>(SearchTruncatedName, description: "Searches that matched more events than the result cap.");
        jobRuns = meter.CreateCounter<long>(JobRunsName, description: "Periodic job runs by job and status.");
        jobDuration = meter.CreateHistogram<double>(JobDurationName, unit: "s", description: "Periodic job run duration.");

        meter.CreateObservableGauge(BreakerOpenName, ObserveBreakers, description: "1 while the breaker is open, else 0.");
        meter.CreateObservableGauge(
            ProviderBudgetRemainingName, ObserveBudgetRemaining, description: "Fraction of today's global provider budget left.");
        meter.CreateObservableGauge(
            WebRiskQuotaUsedName, ObserveWebRiskQuotaUsed, description: "Fraction of today's Web Risk daily cap used.");
        meter.CreateObservableGauge(
            OtpConversionRatioName, ObserveOtpConversion, description: "Verified share of SMS sends over the last hour.");
        meter.CreateObservableGauge(
            ScanPendingEventsName, ObserveScanPending, description: "Events waiting for a link scan (parked publish or join instructions).");
        meter.CreateObservableGauge(
            JobLastSuccessName, ObserveJobLastSuccess, description: "Unix time of the last successful run of each job.");
        meter.CreateObservableGauge(JobIntervalName, ObserveJobIntervals, description: "Configured run interval of each job.");
    }

    public void RecordPublicSetCookieStripped() => publicSetCookieStripped.Add(1);

    public void RecordCachePurge(string result) => cfPurge.Add(1, new KeyValuePair<string, object?>(ResultTag, result));

    public void RecordEventPublished() => eventsPublished.Add(1);

    public void RecordEventAutoHidden(string reason) => eventsAutoHidden.Add(1, new KeyValuePair<string, object?>(ReasonTag, reason));

    public void RecordReport() => reports.Add(1);

    public void RecordOtpSend(string outcome) => otpSends.Add(1, new KeyValuePair<string, object?>(OutcomeTag, outcome));

    public void RecordOtpVerify(string result) => otpVerify.Add(1, new KeyValuePair<string, object?>(ResultTag, result));

    public void SetOtpConversion(int sends, int verified) =>
        Volatile.Write(ref otpConversionRatio, sends <= 0 ? double.NaN : (double)verified / sends);

    public void RecordProviderCall(string provider, string? mapboxMode)
    {
        providerCalls.Add(1, new KeyValuePair<string, object?>(ProviderTag, provider));
        if (mapboxMode is not null)
        {
            mapboxCalls.Add(1, new KeyValuePair<string, object?>(ModeTag, mapboxMode));
        }
    }

    public void SetProviderBudget(string provider, int used, int cap)
    {
        var today = clock.GetCurrentInstant().InUtc().Date;
        budgets[provider] = new BudgetSample(today, cap <= 0 ? 0 : Math.Clamp((double)used / cap, 0, 1));
    }

    public void RecordLinkScan(string verdict, TimeSpan duration)
    {
        linkScans.Add(1, new KeyValuePair<string, object?>(VerdictTag, verdict));
        linkScanDuration.Record(duration.TotalSeconds);
    }

    public void SetBreaker(string name, DateTimeOffset? openUntil) =>
        breakerOpenUntil[name] = openUntil ?? DateTimeOffset.MinValue;

    public void RecordSearch(TimeSpan duration, bool truncated)
    {
        searchDuration.Record(duration.TotalSeconds);
        if (truncated)
        {
            searchTruncated.Add(1);
        }
    }

    public void SetScanPendingEvents(long count) => Volatile.Write(ref scanPendingEvents, count);

    public void RegisterJobInterval(string job, TimeSpan interval) => jobIntervals[job] = interval.TotalSeconds;

    public void RecordJobRun(string job, string status, TimeSpan duration)
    {
        jobRuns.Add(1, new KeyValuePair<string, object?>(JobTag, job), new KeyValuePair<string, object?>(StatusTag, status));
        jobDuration.Record(duration.TotalSeconds, new KeyValuePair<string, object?>(JobTag, job));
        if (status == "succeeded")
        {
            jobLastSuccess[job] = clock.GetCurrentInstant().ToUnixTimeSeconds();
        }
    }

    private IEnumerable<Measurement<long>> ObserveBreakers()
    {
        var now = clock.GetCurrentInstant().ToDateTimeOffset();
        foreach (var (name, openUntil) in breakerOpenUntil)
        {
            yield return new Measurement<long>(openUntil > now ? 1 : 0, new KeyValuePair<string, object?>(NameTag, name));
        }
    }

    private IEnumerable<Measurement<double>> ObserveBudgetRemaining()
    {
        var today = clock.GetCurrentInstant().InUtc().Date;
        foreach (var (provider, sample) in budgets)
        {
            var remaining = sample.Day == today ? 1 - sample.UsedFraction : 1;
            yield return new Measurement<double>(remaining, new KeyValuePair<string, object?>(ProviderTag, provider));
        }
    }

    private IEnumerable<Measurement<double>> ObserveWebRiskQuotaUsed()
    {
        var today = clock.GetCurrentInstant().InUtc().Date;
        if (budgets.TryGetValue("webrisk", out var sample))
        {
            yield return new Measurement<double>(sample.Day == today ? sample.UsedFraction : 0);
        }
    }

    private IEnumerable<Measurement<double>> ObserveOtpConversion()
    {
        var ratio = Volatile.Read(ref otpConversionRatio);
        if (!double.IsNaN(ratio))
        {
            yield return new Measurement<double>(ratio);
        }
    }

    private IEnumerable<Measurement<long>> ObserveScanPending()
    {
        var pending = Volatile.Read(ref scanPendingEvents);
        if (pending >= 0)
        {
            yield return new Measurement<long>(pending);
        }
    }

    private IEnumerable<Measurement<double>> ObserveJobLastSuccess() =>
        jobLastSuccess.Select(pair => new Measurement<double>(pair.Value, new KeyValuePair<string, object?>(JobTag, pair.Key)));

    private IEnumerable<Measurement<double>> ObserveJobIntervals() =>
        jobIntervals.Select(pair => new Measurement<double>(pair.Value, new KeyValuePair<string, object?>(JobTag, pair.Key)));

    private readonly record struct BudgetSample(LocalDate Day, double UsedFraction);
}
