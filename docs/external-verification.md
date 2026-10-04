# External verification

Automated tests cannot cover these checks. Each needs real VPS, Cloudflare, Twilio, Mapbox, Web Risk, R2, or GitHub access. Check each box after you run the check and record the evidence (screenshot, log line, or link).

## Setup

- [ ] GitHub environments exist (`staging`, `production`, `restore-drill`) with required reviewers on `production` and `restore-drill`.
- [ ] GitHub secrets are set per environment: deploy SSH key, host key, R2 settings, `BACKUP_AGE_PRIVATE_KEY`, Cloudflare Pages token.
- [ ] The `TURNSTILE_SITE_KEY` variable is set on the `staging` and `production` GitHub environments. `deploy-web.yml` fails without it.
- [ ] The VPS is logged in to `ghcr.io` with a `read:packages` token.
- [ ] `/opt/hockey-index` is installed with `.env` (mode 0600) and the compose files.
- [ ] The tunnel ingress in `deploy/cloudflared/config.yml` is mirrored in the Cloudflare dashboard.
- [ ] The discover map follows the [OSM tile usage policy](https://operations.osmfoundation.org/policies/tiles/): visible `© OpenStreetMap contributors` attribution, a `Referer` on tile requests, no bulk or automated fetching. Before heavy traffic, move to a commercial tile provider or a self-hosted tile server.
- [ ] A Twilio usage trigger (daily spend alert) exists and emails the owner.
- [ ] A Web Risk quota alert exists in Google Cloud.

## M0: infrastructure

- [ ] `nmap` of the VPS shows only port 22.
- [ ] `https://api.hockeyindex.com/healthz` returns 200 through the tunnel.
- [ ] Access protects staging hosts.
- [ ] The `deploy` user cannot run `docker ps`.
- [ ] From inside `api-staging`, `pg-prod:5432` and `api:8081` fail. From `cloudflared`, `api:8081` fails.
- [ ] VPS IP ports 8080 and 8081 are unreachable.

## M1: identity

- [ ] A real US number and a real CA number sign up on staging.
- [ ] `+44` and `+1-876` attempts leave no Twilio log entry.
- [ ] A screenshot of Twilio geo-permissions is in the runbook or evidence folder.

## M2: venues

- [ ] Mapbox spike gate passes: account, pricing, and terms allow temporary suggestions plus one permanent call per selection.
- [ ] Mapbox dashboard shows temporary calls for typing and one permanent call per selection on staging.

## M4: discovery and SEO

- [ ] Repeated staging searches return `cf-cache-status: HIT`.
- [ ] Repeated `/e/{id}` loads show the Function's API subrequest as a cache hit.
- [ ] The WAF rule blocks the 61st `/e/*` request per minute.
- [ ] The Rich Results Test passes for an event page.
- [ ] `takedown.spec` finishes within 120 seconds on staging, with and without purge.

## M5b: moderation

- [ ] The Web Risk test URL is blocked on staging.

## M6: operations

- [ ] `restore-drill.yml` runs green against real R2.
- [ ] One manual restore on an off-VPS machine is logged in the runbook.
- [ ] R2 holds 14 days of backups, or lifecycle rules are verified plus 3 days of objects.
- [ ] The restore-drill runner choice and PII controls are recorded in the runbook.
- [ ] Each alert in `deploy/observability/alerts.yaml` test-fires to the on-call channel.
- [ ] `twilio-webhook.spec` passes through Cloudflare on staging.
- [ ] `synthetic-dst.yml` is green on prod.
- [ ] A k6 run at 50 rps on `/v1/search` on staging meets p95 under 150 ms, and prod `/healthz` latency is unaffected.
- [ ] The security review has sign-off, or findings are accepted in writing.
- [ ] Launch checklist done: DNS, `robots.txt`, sitemap, privacy page, runbook contacts.
