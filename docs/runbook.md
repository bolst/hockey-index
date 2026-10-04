# Runbook

This runbook covers production operations. Replace every `<placeholder>` before launch.

Conventions:

- The VPS install lives in `/opt/hockey-index`. Secrets live in `/opt/hockey-index/.env` (mode 0600, owner `deploy`).
- The `deploy` user runs one forced command: `deploy.sh <prod|staging> <sha-tag>`. It cannot run `docker ps`.
- Replace `<admin>` with your personal sudo user on the VPS.

## Contacts

| Role | Contact |
|---|---|
| Owner and on-call | `<name, phone, email>` |
| Backup on-call | `<name, phone, email>` |
| Cloudflare account recovery | `<contact>` |
| Twilio support | `<account manager or support URL>` |

## Deploy

GitHub Actions deploys on merge to `main`. The workflow builds images, pushes them to GHCR, and runs the forced SSH command.

To deploy by hand:

1. Pick an image tag that matches `^sha-[0-9a-f]{12}$`.
2. Deploy staging first: `ssh deploy@<vps> staging sha-0123456789ab`.
3. Check `https://api-staging.hockeyindex.com/healthz` through Cloudflare Access.
4. Deploy prod: `ssh deploy@<vps> prod sha-0123456789ab`.

`deploy.sh` pulls images, starts Postgres, runs the one-shot `migrator` container, starts the API, and gates on `:8081/healthz` and `:8081/readyz` on the internal network. The script never runs migrations on API start.

## Rollback

`deploy.sh` rolls back automatically when the health gate fails. It redeploys the previous tag from `state/<env>.tag` and skips migrations.

To roll back by hand, deploy the previous tag:

1. Read the last good tag: `cat /opt/hockey-index/state/prod.tag`.
2. Run `ssh deploy@<vps> prod <previous-tag>`.

Migrations MUST stay backward compatible for one release. If a migration breaks the previous image, restore from backup (see [Restore](#restore)) instead of rolling back.

## Staging

Staging runs as a second compose project on the same VPS with its own network, database, secrets, and provider accounts. Cloudflare Access protects `api-staging.hockeyindex.com`. Staging CPU and memory limits protect prod during load tests. Signup mode on staging is `invite_only`.

## Restore

Backups run nightly at 03:15 UTC in the `backup` container. Each run writes `daily/<db>-<timestamp>.dump.age` to R2. Sundays also write `weekly/`. Only the age public key exists on the VPS.

Freshness and size alerts: `BackupStale` fires after 26 hours. `BackupSizeSuspicious` fires below 50% of the 7-day median.

### Restore drill (monthly and after any backup change)

`.github/workflows/restore-drill.yml` restores the newest dump into a throwaway postgis container and prints counts. It runs in the protected `restore-drill` GitHub environment and needs owner approval.

**PII warning (risk R25).** The drill decrypts production phone numbers and emails. On a GitHub-hosted runner, that data lands on shared infrastructure. The workflow streams the dump through pipes, uploads no artifacts, logs counts only, and discards the runner. Choose one runner policy before launch and record it here:

- [ ] Hosted runner with the controls above.
- [ ] Self-hosted ephemeral runner on hardware you control (preferred).
- [ ] Manual laptop drill only (preferred).

Decision: `<choice, date, owner>`.

### Manual restore on a laptop

Requirements: Docker, `age`, `rclone`, and `postgresql-client` 17. The private key MUST NOT touch the VPS.

1. Start a throwaway database with the init scripts:

   ```sh
   docker run -d --name hi-restore --platform linux/amd64 -p 55432:5432 \
     -e POSTGRES_PASSWORD=drill -e POSTGRES_DB=hockeyindex \
     -v "$PWD/deploy/postgres/init:/docker-entrypoint-initdb.d:ro" \
     postgis/postgis:17-3.5
   ```

2. Export the R2 settings and your private key from the password manager:

   ```sh
   export R2_ENDPOINT=... R2_BUCKET=... R2_ACCESS_KEY_ID=... R2_SECRET_ACCESS_KEY=...
   export AGE_PRIVATE_KEY='AGE-SECRET-KEY-...'
   ```

3. Run the restore:

   ```sh
   PGHOST=127.0.0.1 PGPORT=55432 PGUSER=postgres PGPASSWORD=drill PGDATABASE=hockeyindex \
   RESTORE_MIGRATIONS_DIR=src/api/HockeyIndex.Api/Infrastructure/Persistence/Migrations \
   deploy/restore/restore.sh
   ```

4. Compare the printed row counts with the last known production counts.
5. Remove the container: `docker rm -f hi-restore`.

`restore.sh` picks the newest object under `daily/`. Set `RESTORE_OBJECT=<name>` to pick another. Set `BACKUP_REMOTE=<rclone path>` to read from a different remote.

### Disaster recovery (VPS lost)

Target: RTO 2 hours, RPO 24 hours.

1. Provision a new VPS with the host hardening from `deploy/host/`.
2. Install `/opt/hockey-index`, log in to GHCR, and copy `.env` from the password manager.
3. Start Postgres only: `docker compose up -d pg-prod`.
4. Restore with `restore.sh` into `pg-prod`, using the superuser and `RESTORE_INIT=0`.
5. Deploy the last good tag, then start `cloudflared`.
6. Point the tunnel at the new host if the tunnel token changed.

### Retention

- Nightly: `backup.sh` deletes `daily/` objects older than `BACKUP_RETENTION_DAYS` (default 30, MUST be at least 14).
- Weekly: `weekly/` copies live `BACKUP_WEEKLY_RETENTION_DAYS` (default 84, which is 12 weeks).
- Set R2 lifecycle rules as the primary control: expire `daily/` after 30 days and `weekly/` after 84 days.

### Backup metrics

`backup.sh` posts `backup_last_success_timestamp` and `backup_size_bytes` to a Pushgateway-compatible endpoint. Set `METRICS_PUSH_URL` (and optionally `METRICS_PUSH_USER`, `METRICS_PUSH_PASSWORD`) in `.env`. Grafana Cloud free tier has no push endpoint, so run a Pushgateway behind Grafana Alloy or use another collector, then scrape it. If `METRICS_PUSH_URL` is unset, the script skips the push.

## Key rotation

Rotate on a schedule and after any suspected leak. Never log a key.

### Edge key (`Edge:Key`, `Edge:PreviousKey`)

The Pages Function sends the edge key to the API. The API accepts the current and previous key.

1. Generate a new key: `openssl rand -base64 48`.
2. On the VPS, set `HI_EDGE_PREVIOUS_KEY` to the current `HI_EDGE_KEY`, then set `HI_EDGE_KEY` to the new key. Deploy the API so it accepts both.
3. Update the Pages Function secret that holds the edge key to the new key and redeploy Pages.
4. Wait 24 hours. Check that no request uses the previous key.
5. Remove `HI_EDGE_PREVIOUS_KEY` and deploy again.

### HMAC key (`Hmac:Key`)

The key hashes IPs and tokens. It MUST be at least 32 characters.

1. Generate a key: `openssl rand -base64 48`.
2. Replace `HI_HMAC_KEY` in `.env` and deploy.
3. Expect side effects: outstanding magic links and IP-based rate-limit state reset. Hosts stay signed in.

### Data Protection keys

Keys live in the `data_protection_keys` table and rotate automatically every 90 days. Old keys stay for decryption.

To force rotation after a leak:

1. Delete the leaked key rows (`delete from data_protection_keys where ...`) as `hi_migrator`.
2. Restart the API. It creates a new key.
3. Expect every Host to sign in again.

### Backup age key pair

1. Run `age-keygen -o new.key` on a trusted machine.
2. Store the private key in the password manager and in the `BACKUP_AGE_PRIVATE_KEY` secret of the `restore-drill` environment.
3. Set `BACKUP_AGE_RECIPIENT` on the VPS to the new public key and run `docker compose up -d backup`.
4. Keep the old private key until all backups encrypted to it leave retention (84 days).

### Other secrets

Rotate provider tokens (Twilio, Mapbox, Web Risk, Postmark, Cloudflare purge token, R2 keys) in the provider console, then update `.env` and redeploy. Rotate R2 keys with the restore drill in mind: the drill needs read access.

## Circuit breakers

Breakers live in the `breakers` table (`name`, `open_until`, `reason`, `opened_by`). The API opens them on abuse signals. `hi_breaker_open{name}` reports state.

To inspect:

```sql
select name, open_until, reason, opened_by from breakers where open_until > now();
```

To open a breaker by hand, for example `otp_sms`:

```sql
insert into breakers (name, open_until, reason, opened_by, updated_at)
values ('otp_sms', now() + interval '2 hours', 'manual: <why>', 'admin', now())
on conflict (name) do update
set open_until = excluded.open_until, reason = excluded.reason, opened_by = excluded.opened_by, updated_at = now();
```

To close a breaker after you remove the cause:

```sql
update breakers set open_until = null, updated_at = now() where name = 'otp_sms';
```

Run SQL as `hi_migrator` from inside the Postgres container. Do not close a breaker before you find the cause.

While the link-scan breaker is open, publish requests wait as drafts and publish on their own after the breaker closes. Alert `ScanPending` tracks the wait.

## Budget caps

Daily caps live in the `provider_usage` table and in configuration keys `Providers__Caps__<provider>__Global` and `Providers__Caps__<provider>__PerHost`. Defaults: Mapbox temporary 3000 global and 200 per host, Mapbox permanent 200 and 20, Web Risk 5000 and 200, Twilio Verify 500, Twilio Lookup 500.

To raise a cap:

1. Confirm the provider bill allows it.
2. Set the environment key in `.env` and deploy.
3. Record the change and reason in `<change log>`.

To see usage:

```sql
select provider, sum(calls) as calls, max(cap) as cap from provider_usage where day = current_date group by provider;
```

A spent cap opens the matching breaker for the rest of the UTC day. Alert `ProviderBudgetLow` fires below 10% remaining. Alert `WebRiskQuotaHigh` fires above 80%.

## SMS pumping

Signals: alert `OtpSendSurge`, alert `OtpConversionDrop`, a Twilio usage trigger email, or an open `otp_sms` breaker.

1. Open the `otp_sms` breaker by hand (see [Circuit breakers](#circuit-breakers)).
2. Set `SIGNUP_MODE=invite_only` in `.env` and deploy. New sign-ups stop; existing Hosts keep working.
3. In Twilio, check Messaging logs for destination prefixes. Block the range in `blocklist` (`kind = 'phone'`) and in Twilio geo-permissions.
4. Check Fraud Guard settings and the daily spend trigger.
5. After traffic normalizes for 24 hours, close the breaker and set `SIGNUP_MODE=open`.

The `TwilioUsageWebhook` at `/v1/internal/twilio-usage` opens the breaker when Twilio sends a usage trigger. Twilio signs the request; the API validates the signature against `TWILIO_CALLBACK_URL`. If the URL changes, update both sides.

## Provider outage

| Provider | Effect | Action |
|---|---|---|
| Web Risk | Publishes wait as drafts; edits keep the last clean text live | Wait, or check quota. Alert `ScanPending` at 30 minutes. |
| Twilio | No sign-in codes | Check status page. Do not raise caps blindly. |
| Mapbox | No new venue lookups; existing venues work | Wait. Fallback to Geocodio/Esri is in the ADR follow-ups. |
| Postmark | No email login or confirmation | Check status page; phone login still works. |
| Cloudflare purge | Takedown relies on TTL (at most 2 minutes) | See [Purge fallback](#purge-fallback). |

Never set the system to publish unscanned content.

## Purge fallback

Public API responses carry a `Cache-Tag` and a TTL of at most 60 seconds. Purge by tag speeds up takedown. The TTL alone meets the 2-minute takedown target.

If tag purge fails (alert `PurgeFailures`, or your Cloudflare plan lacks tag purge):

1. Confirm `CLOUDFLARE_ZONE_ID` and `CLOUDFLARE_PURGE_TOKEN` are correct and the token has Cache Purge permission.
2. Switch to URL purge. The API purges by tag today. URL purge of each event URL and the known search URLs is the designed fallback and needs a code change (ADR follow-up 2). Until it ships, rely on the TTL.
3. Verify with `curl -sI https://api.hockeyindex.com/v1/events/<id>` and read `cf-cache-status`.
4. If purge still fails, wait up to 2 minutes for the TTL. Do not disable the TTL.

Open question Q-new-1 tracks whether the plan supports tag purge.

## Moderation

Reports: three distinct reporters hide an event. Admins review the queue, restore or ban, and every action writes to the append-only `audit_log`.

- URL-less scams (for example "send an e-transfer to this email") escape link scanning. Hide the event, ban the Host if repeated, and record the pattern in `<scam pattern list>`.
- A takedown request: hide the event first, then investigate. Check the public page after two minutes.
- Phone number change (lost phone, no confirmed email): verify identity out of band, change the number manually, and write an `audit_log` entry.
- Account deletion request: delete the Host as admin. Events purge; audit rows stay without PII.

## Cloudflare

- DNS: `api.hockeyindex.com` and `api-staging.hockeyindex.com` are proxied CNAMEs to the tunnel.
- Tunnel ingress: `deploy/cloudflared/config.yml` is the source. Mirror it in the Cloudflare dashboard and keep both in sync.
- Access: policy on `api-staging.hockeyindex.com`, with a bypass for `/v1/internal/twilio-usage`.
- Cache rules: four public paths only. A Transform Rule strips `Cookie` on them.
- WAF: rate limit `hockeyindex.com/e/*` at 60 requests per minute per IP.
- Pages: static SPA plus the `/e/:id/:slug` Function. The discover map loads OpenStreetMap tiles from `tile.openstreetmap.org`; the CSP `img-src` in `functions/_middleware.ts` MUST allow that host and `Referrer-Policy` MUST NOT be `no-referrer`.
- Verify after any change: `cf-cache-status: HIT` on repeated searches and no `Set-Cookie` on public responses.
