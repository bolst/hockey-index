import { execFileSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';

/**
 * Helpers that reach into the local docker compose dev stack (deploy/compose.dev.yaml).
 * Every database call goes through `docker exec` into the compose Postgres container and refuses to run
 * against anything that is not the `hockey-index-dev` compose project.
 */
export const COMPOSE_PROJECT = 'hockey-index-dev';
export const PG_CONTAINER = process.env['E2E_PG_CONTAINER'] ?? 'hockey-index-dev-pg-dev-1';
export const RUN_STATE = 'e2e/.auth/run.json';
/** The fixture host signed in by global-setup.ts through the API; specs reuse it for seeded events. */
export const FIXTURE_HOST_STATE = 'e2e/.auth/fixture-host.json';
/** Written by report.spec.ts: the event hidden by reports, which admin.spec.ts restores. */
export const REPORT_STATE = 'e2e/.auth/report.json';
/** Prefix of every event title the suite creates; global setup archives leftovers from earlier runs. */
export const E2E_TITLE_PREFIX = 'E2E ';

export interface RunState {
  runId: string;
  /** Signs in through the UI in host-signup.spec.ts. */
  uiHost: { input: string; display: string };
  /** Gets a wrong code in host-signup.spec.ts, so it never becomes a host. */
  wrongCodePhone: string;
  /** Owns the events that API-seeded specs create. */
  fixtureHost: { phone: string; id: string };
  /** Banned and unbanned in admin.spec.ts. */
  banHost: { phone: string; id: string };
}

export function readRunState(): RunState {
  if (!existsSync(RUN_STATE)) {
    throw new Error(`${RUN_STATE} is missing: run the suite through playwright.config.ts so global-setup.ts runs first.`);
  }
  return JSON.parse(readFileSync(RUN_STATE, 'utf8')) as RunState;
}

function docker(args: string[], input?: string): string {
  return execFileSync('docker', args, { encoding: 'utf8', input, stdio: ['pipe', 'pipe', 'pipe'] });
}

/** Throws unless the Postgres container belongs to the local compose dev project. */
export function assertLocalComposeDatabase(): void {
  let project: string;
  try {
    project = docker(['inspect', '--format', '{{index .Config.Labels "com.docker.compose.project"}}', PG_CONTAINER]).trim();
  } catch (error) {
    throw new Error(
      `Cannot inspect ${PG_CONTAINER}. Start the dev stack (docker compose -f deploy/compose.dev.yaml up -d) ` +
        `and check DOCKER_HOST. ${(error as Error).message}`,
    );
  }
  if (project !== COMPOSE_PROJECT) {
    throw new Error(`${PG_CONTAINER} belongs to compose project "${project}", not ${COMPOSE_PROJECT}; refusing to touch it.`);
  }
}

/** Runs SQL in the dev database and returns unaligned, tuples-only output (one row per line, `|` separated). */
export function sql(statement: string): string {
  assertLocalComposeDatabase();
  return docker(
    ['exec', '-i', PG_CONTAINER, 'psql', '-U', 'postgres', '-d', 'hockeyindex', '-v', 'ON_ERROR_STOP=1', '-qAt', '-f', '-'],
    statement,
  ).trim();
}

export function sqlLiteral(value: string): string {
  return `'${value.replace(/'/g, "''")}'`;
}

/**
 * Does what ArchiveSweepJob does once an event has ended: moves the matching published, cancelled or hidden
 * events one day into the past, archives them and records the status change. The job runs every 15 minutes
 * and has no trigger endpoint, so tests use this instead of waiting.
 */
export function archiveEvents(where: string): number {
  const output = sql(`
    BEGIN;
    CREATE TEMP TABLE ending ON COMMIT DROP AS
      SELECT id, host_id, status AS from_status,
             GREATEST(ends_at - now() + interval '1 day', interval '0') AS shift
      FROM events
      WHERE status IN ('published', 'cancelled', 'hidden') AND (${where});
    UPDATE events e SET
      starts_at = e.starts_at - t.shift,
      ends_at = e.ends_at - t.shift,
      starts_local = e.starts_local - t.shift,
      ends_local = e.ends_local - t.shift,
      hidden_reason = CASE WHEN e.status = 'hidden' THEN e.hidden_reason END,
      hidden_from_status = NULL,
      status = 'archived',
      archived_at = now(),
      updated_at = now()
    FROM ending t WHERE e.id = t.id;
    INSERT INTO event_status_changes (id, event_id, host_id, from_status, to_status, actor_id, reason, at)
      SELECT gen_random_uuid(), id, host_id, from_status, 'archived', NULL, NULL, now() FROM ending;
    SELECT count(*) FROM ending;
    COMMIT;
  `);
  return Number(output.split('\n').filter(Boolean).pop() ?? '0');
}

export function randomTestNetIp(): string {
  const octet = () => Math.floor(Math.random() * 254) + 1;
  return `198.${18 + Math.floor(Math.random() * 2)}.${octet()}.${octet()}`;
}
