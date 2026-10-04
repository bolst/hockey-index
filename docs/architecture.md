# Architecture

Hockey Index lists hockey scrimmages, leagues, and tournaments in the US and Canada. Anyone can search. Phone-verified Hosts publish listings after a link scan passes.

Sections below name the matching sections in the [implementation plan](../.omc/plans/hockey-index-plan.md).

## Components

```
Browser --> Cloudflare Pages (Angular SPA + /e/:id/:slug Function)
   |                               |
   +--> api.hockeyindex.com -------+   (Cloudflare cache, WAF)
              |
         Cloudflare Tunnel --> cloudflared --> api (ASP.NET Core, .NET 10)
                                                  |
                                             pg-prod (PostgreSQL 17 + PostGIS)
                                                  ^
                                             backup (pg_dump | age | rclone -> R2)
```

- **API** (`src/api`): vertical-slice Minimal APIs over one `AppDbContext`. Plan section 2.3(a).
- **Web** (`src/web`): static Angular SPA. A Pages Function renders SEO HTML for event pages. Plan section 2.3(b).
- **Jobs**: in-process `BackgroundService` jobs with transaction-scoped advisory locks. Plan section 2.3(c).
- **Venues**: Mapbox v6 with one permanent call per selection. Plan section 2.3(d).
- **Routing**: separate API host, static CORS on public responses. Plan section 2.3(e).
- **Database and schema**: plan section 4.
- **API surface, caching, rate limits**: plan section 5.
- **Networks**: `edge` holds `cloudflared` and the APIs. Databases sit on `internal: true` networks per environment. Plan section 2.3(f).
- **Deploy**: Docker Compose on one VPS, forced-command SSH deploys. See [runbook](runbook.md#deploy).
- **Observability**: Serilog JSON logs, OTel metrics, Grafana alerts. Plan section 9.4 and `deploy/observability/alerts.yaml`.
- **Risks**: plan section 7.

## Decisions

See [ADR 0001](adr/0001-core-architecture.md).

## Checks that need real services

See [external verification](external-verification.md).
