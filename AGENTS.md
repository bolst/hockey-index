# AGENTS.md

Guidance for coding agents working in this repo. For setup and product context, read `README.md` and `docs/architecture.md` first.

## Stack

- **API:** .NET 10 Minimal APIs, EF Core with Npgsql, PostGIS and NodaTime. Lives in `src/api`.
- **Web:** Angular 21 with Angular Material 3, deployed to Cloudflare Pages with Pages Functions. Lives in `src/web`.
- **Deploy:** Docker Compose on one VPS behind a Cloudflare Tunnel. Lives in `deploy/`.

## Layout

| Path | Contents |
| --- | --- |
| `src/api/HockeyIndex.Api/Features/<Area>` | Vertical slices: one file per endpoint, plus `<Area>Endpoints.cs` |
| `src/api/HockeyIndex.Api/Domain` | Entities and domain rules |
| `src/api/HockeyIndex.Api/Infrastructure` | Auth, caching, HTTP, observability, persistence, rate limiting |
| `src/api/HockeyIndex.Api/Integrations` | External providers (Twilio, Turnstile, Web Risk, geocoding, email, Cloudflare) and their fakes |
| `src/api/HockeyIndex.Api.Tests.Unit` | Unit tests |
| `src/api/HockeyIndex.Api.Tests.Integration` | Integration tests against a Testcontainers PostGIS database |
| `src/web/src/app/features` | One folder per page |
| `src/web/src/app/shared` | Shared components and services |
| `src/web/functions` | Pages Functions: CSP middleware, sitemap proxy, `/e/{id}` SEO |
| `src/web/test/functions` | Vitest tests for the Pages Functions |
| `src/web/e2e` | Playwright specs |
| `deploy/` | Compose files, Postgres, backup and restore, host scripts, observability |
| `docs/` | Architecture, runbook, ADRs, external verification, privacy |
| `.omc/` | Plan and spec history. Do not edit as part of feature work. |

## Commands

Run every `dotnet` command from `src/api`. Run every `npm` command from `src/web`. These match CI (`.github/workflows/ci.yml`); a change MUST pass all of them.

```bash
# API
dotnet tool restore
dotnet build -c Release
dotnet test
dotnet ef migrations has-pending-model-changes --project HockeyIndex.Api

# Web
npm ci
npm run lint
npm test
npm run build

# Deploy checks, from the repo root
bash deploy/scripts/check-no-ports.sh
docker compose --env-file deploy/.env.example -f deploy/compose.yaml config --quiet
```

- Integration tests need a running Docker daemon.
- Warnings are errors (`TreatWarningsAsErrors`). Fix the warning; MUST NOT suppress it without a stated reason.

## Dev stack

To run the full stack locally, start it from the repo root:

```bash
docker compose -f deploy/compose.dev.yaml up --build
```

- The API listens on `:8080`; health checks on `:8081`.
- Fake providers are on. Every OTP is `123456`. The bootstrap admin phone is `+14165550100`.
- In `src/web`, `npm start` serves the app; `npm run e2e` runs Playwright against the dev stack.

## API conventions

- Add an endpoint as a new file in `Features/<Area>` with a static `Map` method. Register new areas in `Features/FeatureEndpoints.cs`.
- Public endpoints MUST add `.CachePublic(...)` and `.RequireRateLimiting(RateLimitPolicies.PublicRead)`.
- Set package versions only in `Directory.Packages.props`.
- MUST NOT edit an existing migration. To change the schema, edit the model and add a migration:
  ```bash
  dotnet ef migrations add <Name> --project HockeyIndex.Api
  ```
- Use NodaTime types for time. MUST NOT use `DateTime.Now` or `DateTime.UtcNow`.
- Postgres advisory lock keys in use: 1–6 OTP, 7 venue, 8 jobs, 9 host publish, 10 reports. Pick an unused key for a new lock.
- Fake providers and the bootstrap admin MUST stay blocked in Production by `StartupGuards`.

## Web conventions

- Components are standalone, `OnPush`, and use signals. Follow the pattern of the nearest existing page.
- Follow Material 3. Use `--mat-sys-*` tokens for colour, type and shape; MUST NOT hard-code colours.
- Every page MUST work at phone width and in dark mode.
- MUST NOT render event text as HTML.
- The map uses Leaflet with OpenStreetMap tiles (`features/discover/discover-map.ts`). To change the tile host, also update `img-src` in the CSP. E2E specs that show the map MUST import `test` from `e2e/fixtures.ts`, which stubs tile requests.
- The CSP lives in `functions/_middleware.ts`. To change it, also update the exact-string test in `test/functions/middleware.spec.ts`. Inline styles need the per-request nonce.
- MUST NOT add a `/*` rule to `public/_redirects`; it causes a redirect loop. Pages already serves `index.html` for deep links.

## E2E tests

- Each spec is its own Playwright project in `e2e/playwright.config.ts`. To add a spec, add a project for it.
- Global setup clears `otp_log` so rate limits do not leak between runs.
- To run one project: `npm run e2e -- --project=<name>`.
- The `staging` project (`takedown`, `twilio-webhook`) needs real Cloudflare and runs only against staging.

## Docs

Update the matching doc in the same change:

- Operational steps: `docs/runbook.md`.
- Checks that need real external accounts: `docs/external-verification.md`.
- Architecture decisions: a new ADR in `docs/adr/`.
- Data retention changes: `docs/privacy/data-retention.md`.

## Rules

- MUST NOT commit secrets. Use `deploy/.env.example` for new settings and GitHub environment secrets for CI.
- MUST NOT publish ports from Compose services; `check-no-ports.sh` enforces this.
- MUST NOT commit or push unless the user asks.
