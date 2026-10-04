# Hockey Index

A public directory of hockey scrimmages, leagues, and tournaments in the US and Canada. Anyone can search near them. Phone-verified Hosts publish listings after an automatic link scan passes.

Stack: ASP.NET Core (.NET 10) Minimal API, PostgreSQL 17 with PostGIS, Angular 21 on Cloudflare Pages, Docker Compose on one VPS behind a Cloudflare Tunnel.

## Repository layout

| Path | Contents |
|---|---|
| `src/api` | API solution. `HockeyIndex.sln`, `Directory.Build.props`, `Directory.Packages.props`, and `global.json` live here. Run every `dotnet` command from this folder. |
| `src/web` | Angular app, Cloudflare Pages Functions (`functions/`), and their tests (`test/functions/`). |
| `deploy` | Compose files, Dockerfiles, host hardening, backup and restore scripts, alert rules. |
| `docs` | Runbook, architecture, ADRs, verification checklist. |
| `.github/workflows` | CI, deploys, restore drill, synthetic DST check. |

## Local development

Requirements: Docker, .NET SDK 10, Node 22.

1. Start Postgres, run migrations, and start the API with fake providers. From the repository root:

   ```sh
   docker compose -f deploy/compose.dev.yaml up --build
   ```

   With the standalone Compose binary, run `docker-compose` instead. The `migrator-dev` container applies migrations and exits with code 0. The API then listens on `http://localhost:8080`, with health checks at `http://localhost:8081/healthz`.

   On Apple silicon, Postgres runs under amd64 emulation, so the first start is slow. To reset the database, run `docker compose -f deploy/compose.dev.yaml down -v`.

   The migrator logs `libgssapi_krb5.so.2` and a failed `__EFMigrationsHistory` query on first run. Both are expected.

2. Start the web app:

   ```sh
   cd src/web
   npm ci
   npm start
   ```

   Open `http://localhost:4200` in Chrome or Firefox. The sign-in cookie uses the `__Host-` prefix, which needs a browser that treats `http://localhost` as secure.

### Fake providers

`HI_FAKE_PROVIDERS=true` replaces the paid services. The API refuses to start in Production with fakes.

- OTP code: `123456`.
- Twilio Lookup by the last four digits of the number: `0001` landline, `0002` non-fixed VoIP, `0003` toll-free, `0004` fixed VoIP, anything else mobile.
- Turnstile: an empty token or a token starting with `fail` fails. Any other token passes.
- Postmark keeps messages in memory.
- Admin: `deploy/compose.dev.yaml` sets `Admin__BootstrapPhones__0` to `+14165550100`, so signing in with `+1 416-555-0100` gives you the admin pages. The API refuses to start in Production when this setting is present.

### End-to-end tests

The Playwright suite in `src/web/e2e` runs against the dev stack from step 1. To run it:

1. Start the compose stack and wait for the API health check at `http://localhost:8081/healthz`.
2. If your Docker socket is non-standard, export `DOCKER_HOST` so `docker exec` reaches the compose containers.
3. From `src/web`, install the browser once, then run the suite:

   ```sh
   npx playwright install chromium
   npm run e2e
   ```

Playwright starts three servers, or reuses ones already running:

- `ng serve` on `http://localhost:4200` for most specs.
- A production build served by `wrangler pages dev` on `http://localhost:8788` for the CSP check.
- The same build on `http://localhost:8789`, with `API_ORIGIN=http://localhost:8080`, so the `seo` spec can run the Pages Functions for event pages and `sitemap.xml`.

Global setup (`e2e/global-setup.ts`) makes the suite rerunnable. It refuses to run unless the API is on localhost and the Postgres container belongs to the `hockey-index-dev` compose project. It then:

- deletes all rows from `otp_log`, so SMS rate limits start fresh;
- archives events from earlier runs (titles start with `E2E `) and marks their reports reviewed;
- picks unused `555-01xx` phone numbers for this run's hosts, because each host may publish 5 events a day;
- signs in the fixture hosts through the API and saves their storage state under `e2e/.auth/`.

Each run uses up 3 test phones, so they run out after about 99 runs. To start over, reset the database with `docker compose -f deploy/compose.dev.yaml down -v`. To use a different Postgres container name, set `E2E_PG_CONTAINER`.

`takedown.spec.ts` and `twilio-webhook.spec.ts` run only against staging and skip unless `STAGING_BASE_URL` is set. They also read:

- `STAGING_API_URL`: the staging API origin, without `/v1`.
- `STAGING_ADMIN_STATE`: a Playwright storage state file for a staging admin session.
- `CF_ACCESS_CLIENT_ID` and `CF_ACCESS_CLIENT_SECRET`: a Cloudflare Access service token, if staging sits behind Access.
- `STAGING_TAKEDOWN_EVENT_ID`: a published event that the takedown spec hides and then restores.
- `STAGING_TWILIO_AUTH_TOKEN` and, optionally, `STAGING_TWILIO_CALLBACK_URL`: used to sign the usage-trigger webhook. The spec closes the `otp_sms` breaker when it ends.

## Tests

API, from `src/api`:

```sh
dotnet build
dotnet test
```

Integration tests use Testcontainers with `postgis/postgis:17-3.5`. Docker MUST be running. If your Docker config or socket is non-standard, set `DOCKER_HOST` and `TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/var/run/docker.sock`. On Apple silicon, pull the amd64 image first: `docker pull --platform linux/amd64 postgis/postgis:17-3.5`.

Web, from `src/web`:

```sh
npm test
npm run lint
npm run build
```

Deploy checks:

```sh
bash -n deploy/backup/backup.sh deploy/restore/restore.sh
deploy/scripts/check-no-ports.sh
```

## Backups

`deploy/backup/backup.sh` encrypts a nightly `pg_dump` with an age public key and uploads it to R2. `deploy/restore/restore.sh` restores it anywhere you hold the private key. See [the runbook](docs/runbook.md#restore).

## Documentation

- [Runbook](docs/runbook.md): deploy, rollback, restore, key rotation, breakers, incidents.
- [Architecture](docs/architecture.md)
- [ADR 0001: core architecture](docs/adr/0001-core-architecture.md)
- [External verification checklist](docs/external-verification.md)
- [Data retention](docs/privacy/data-retention.md)
- [Alert rules](deploy/observability/alerts.yaml)
