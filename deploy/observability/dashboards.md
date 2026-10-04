# Dashboards

Build three Grafana dashboards from the metrics in plan section 9.4. Alert rules live in `alerts.yaml`.

- **Abuse:** OTP funnel (`hi_otp_sends_total`, `hi_otp_verify_total`, `hi_otp_conversion_ratio`), breakers (`hi_breaker_open`), budgets (`hi_provider_calls_total`, `hi_provider_budget_remaining`, `hi_webrisk_quota_used`, `hi_mapbox_calls_total`), link scans (`hi_link_scan_total`, `hi_link_scan_duration_seconds`, `hi_scan_pending_events`), reports (`hi_reports_total`), auto-hides (`hi_events_auto_hidden_total`).
- **Product:** publishes (`hi_events_published_total`), active listings, searches (`hi_search_duration_seconds`, `hi_search_truncated_total`), Cloudflare cache hit ratio.
- **Health:** request latency and errors, job runs and duration (`hi_job_runs_total`, `hi_job_duration_seconds`), job staleness (`hi_job_last_success_timestamp`, `hi_job_interval_seconds`), backups (`backup_last_success_timestamp`, `backup_size_bytes`), purges (`hi_cf_purge_total`), disk.

## Export

The API pushes metrics over OTLP (`OTEL_EXPORTER_OTLP_ENDPOINT`) from the meter `HockeyIndex.Api`. It serves no scrape endpoint on port 8080 or 8081.
Job series carry the tag `job_name`, not `job`, because OTLP ingest reserves `job` for the service.
`backup_*` series come from the backup container; `http_server_request_duration_seconds` comes from ASP.NET Core instrumentation.
A test (`ObservabilityMetricsTests`) fails if `alerts.yaml` or this page names an `hi_` metric the API does not register.
