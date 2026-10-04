# ADR 0001: Core architecture

- Status: accepted (2026-10-03)
- Source: plan section 11 (`.omc/plans/hockey-index-plan.md`), consensus of Architect (sound with changes) and Critic (approve with changes).

## Decision

Build one ASP.NET Core (.NET 10) Minimal API with vertical slices over one `AppDbContext` on PostgreSQL 17 with PostGIS. Deploy it with Docker Compose on one 4 vCPU / 8 GB VPS, reachable only through a Cloudflare Tunnel. Run a separate staging compose project on the same host.

Serve the Angular SPA statically from Cloudflare Pages. A non-caching Pages Function renders SEO content for event URLs.

The API lives on `api.hockeyindex.com`. Public responses carry no credentials, send static `Access-Control-Allow-Origin: *`, skip authentication, and are edge-cached for at most 60 seconds. Tag purge speeds up takedown. The TTL alone bounds the takedown target at 2 minutes.

Other decisions:

- Route every event status change through one `EventTransitions` service.
- Never publish unscanned content. A provider outage parks publish requests as drafts.
- Run background work as in-process `BackgroundService` jobs guarded by transaction-scoped advisory locks.
- Persist Data Protection keys in Postgres.
- Source venues from Mapbox v6: temporary suggestions, one permanent call on select. Geocodio or Esri is the fallback.
- Use Twilio Verify and Lookup behind Turnstile, with serialized database limits, daily budgets, and breakers.
- Use Google Web Risk for URL reputation (spec amendment), with per-host daily caps and a verdict cache.
- Isolate prod and staging on the VPS with separate `internal: true` networks. Only `cloudflared` and the two APIs share `edge`. Health and ops endpoints listen on the internal network only.
- The Pages Function validates ids before it calls the API and uses a global edge rate-limit bucket behind a per-IP WAF rule.

## Drivers

1. Solo-owner operational load on a single VPS.
2. Abuse and cost exposure: SMS pumping, malicious links, provider bills.
3. Licensing compliance for venue data and URL reputation.

## Alternatives considered

- API: layered architecture with MediatR; MVC controllers.
- Rendering: `@angular/ssr` on the VPS; build-time prerender; `@angular/ssr` on Workers.
- Routing: same-origin `/v1` through a Pages Function proxy.
- Jobs: Hangfire; Quartz.NET; session-level advisory locks.
- Geocoding: Google Places with `place_id` only; self-hosted Photon or Nominatim; Geocodio or Esri (kept as fallback).
- Scan failure policy: fail-open with later auto-hide; a new `pending` status.
- URL reputation: Google Safe Browsing (non-commercial license).
- Edge key as a full rate-limit exemption.
- One flat compose network for prod and staging.
- Global-only provider caps.

## Why chosen

- Vertical slices keep endpoint policy beside handlers and expose PostGIS and range operators directly.
- The edge Function meets AC-DS-8 without Node on the VPS.
- A separate API host keeps zone caching and purge simple. Static CORS avoids Cloudflare's lack of `Vary: Origin` support.
- TTL-bound caching keeps the takedown target even when purge fails.
- Parking publishes as drafts satisfies AC-LC-3 and closes the outage abuse window.
- Transaction-scoped locks survive Npgsql pool resets.
- Mapbox permanent results permit storage. Splitting temporary and permanent calls controls cost.
- Network separation and per-host caps contain the two shared-resource risks of the single-box design.

## Consequences

Positive:

- One deployable plus one static site, at low cost.
- Postgres enforces most invariants. Row and advisory locks close races.
- Deploys keep sessions.

Negative:

- Rink-name search works only after a Host creates the venue.
- Two renderers produce event content.
- No job dashboard.
- The single VPS is a point of failure (RTO about 2 hours, RPO 24 hours) shared with staging.
- Web Risk outages or a host's daily cap delay publishing.
- Each event page view invokes a Function.
- Admin restores can exceed the active cap (audited).
- A CI restore drill on hosted runners handles production PII unless you choose the self-hosted or laptop option.

Neutral:

- Provider interfaces (`IGeocoder`, `IUrlReputationProvider`, `IEmailSender`, `ISmsVerifier`) keep swaps local.

## Open-question defaults

Open questions live in `.omc/plans/open-questions.md`. Until the owner decides, the system uses these defaults:

| Question | Default |
|---|---|
| Q1: `maxFee` filter with unknown fee | Exclude events with `fee_cents IS NULL` |
| Q2: fail-closed on scan outage | Closed as moot: every publish waits for a clean scan |
| Q3: landline and toll-free numbers | Reject at Lookup |
| Q4: daily budget ceilings | Code defaults in `ProviderBudget` (Mapbox temporary 3000, permanent 200, Web Risk 5000, Twilio 500); set real values before launch |
| Mapbox spike | Fall back to Geocodio, then Esri, if the gate fails |
| Q-new-1: Cache-Tag purge availability | Function does not cache; URL purge is the fallback; TTL bounds takedown |
| Q-new-2: phone number change | Not self-service; admin changes it and writes `audit_log` |
| Q-new-3: self-service account deletion | Request by email; admin deletes; audit rows keep no PII |

## Follow-ups

1. M2 spike result; fall back to Geocodio or Esri if needed.
2. Confirm Cache-Tag purge on the plan (Q-new-1); build URL purge if tags are unavailable.
3. Phone number change flow (Q-new-2) and self-service account deletion for PIPEDA and CCPA (Q-new-3).
4. Seed venues from OSM `leisure=ice_rink` with ODbL attribution.
5. WAL-G continuous archiving (RPO in minutes); move staging off the prod VPS when budget allows.
6. Twilio Lookup reassigned-number check.
7. Keyword heuristics that flag URL-less payment scams for admin review.
8. A `fee_note` field if fee feedback warrants it.
9. Re-evaluate `@angular/ssr` on Workers if Search Console reports thin content.
10. Tune breaker thresholds, per-host caps, and the edge bucket after the first 30 days.
11. Decide the restore-drill runner (hosted with controls, self-hosted, or laptop) before launch.

Added during implementation:

12. Done (US-011): **CSP and Angular styles.** `functions/_middleware.ts` creates a 128-bit nonce for each HTML response and sends `style-src 'self' 'nonce-…'`. `HTMLRewriter` sets `ngCspNonce` on `<app-root>`, so Angular tags its runtime `<style>` elements with that nonce. HTML responses get `Cache-Control: no-store` without `ETag` or `Last-Modified`, and document requests lose their conditional headers. This stops a cached body from pairing with a new nonce. Other responses get `style-src 'self'`. `'unsafe-inline'` is not used. The built `index.html` has no inline scripts or styles, so `script-src` needs no nonce. Part B's Playwright check MUST confirm zero CSP violations.
13. **Angular 21 versus 22.** The web app uses Angular 21.2 because Angular 22 needs Node 22.22.3 or later, and the local toolchain has Node 22.22.2. Upgrade to 22 when CI and local Node meet the floor.
14. Done: `compose.yaml` and `compose.staging.yaml` map `HI_EDGE_PREVIOUS_KEY` to `Edge__PreviousKey` for zero-downtime key rotation.
15. Export `hi_job_interval_seconds` next to `hi_job_last_success_timestamp`, so the `JobStale` alert can compare against the interval.
16. Register the remaining plan 9.4 metrics in the API through OTel `Meter` (later step).
