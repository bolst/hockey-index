import { mkdirSync, rmSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';
import { signInByApi } from './api-fixtures';
import { API_BASE } from './auth-helpers';
import {
  E2E_TITLE_PREFIX,
  FIXTURE_HOST_STATE,
  REPORT_STATE,
  RUN_STATE,
  RunState,
  archiveEvents,
  assertLocalComposeDatabase,
  sql,
  sqlLiteral,
} from './dev-stack';
import { FLOW_STATE, HOST_STORAGE_STATE } from './playwright.config';

const AREA_CODES = ['416', '647', '437'];

/** Fictional 555-01xx numbers the dev API has not seen yet. The admin bootstrap phone is +1 416 555 0100. */
function unusedPhones(count: number): string[] {
  const used = new Set(sql('SELECT phone_number FROM hosts').split('\n'));
  const free: string[] = [];
  for (const area of AREA_CODES) {
    for (let line = 101; line <= 199; line++) {
      const e164 = `+1${area}5550${line}`;
      if (!used.has(e164)) {
        free.push(e164);
      }
    }
  }
  if (free.length < count) {
    throw new Error('No unused 555-01xx test phones left; reset the dev database (docker compose down -v).');
  }
  return free.slice(0, count);
}

function spaced(e164: string): { input: string; display: string } {
  const area = e164.slice(2, 5);
  const line = e164.slice(8);
  return { input: `+1 ${area} 555 ${line}`, display: `+1 ${area}-555-${line}` };
}

/**
 * Makes `npm run e2e` rerunnable against the long-lived dev database:
 * - clears otp_log so the per-phone, per-IP and per-prefix SMS limits start fresh;
 * - archives events left by earlier runs, so search stays under its 100-result cap, and marks their
 *   reports reviewed, so the moderation queue holds only this run's events;
 * - picks hosts nobody has used, because each host may publish only 5 events a day.
 */
export default async function globalSetup(): Promise<void> {
  if (!API_BASE.startsWith('http://localhost:')) {
    throw new Error(`global-setup only runs against the local dev API, not ${API_BASE}.`);
  }
  assertLocalComposeDatabase();

  sql('DELETE FROM otp_log;');
  const e2eTitle = `title LIKE ${sqlLiteral(`${E2E_TITLE_PREFIX}%`)}`;
  archiveEvents(e2eTitle);
  sql(`UPDATE reports SET reviewed_at = now() WHERE reviewed_at IS NULL AND event_id IN (SELECT id FROM events WHERE ${e2eTitle});`);

  for (const stale of [HOST_STORAGE_STATE, FLOW_STATE, REPORT_STATE, FIXTURE_HOST_STATE]) {
    rmSync(stale, { force: true });
  }
  mkdirSync(dirname(RUN_STATE), { recursive: true });

  const [uiHost, wrongCodePhone, fixturePhone, banPhone] = unusedPhones(4);
  const fixture = await signInByApi(fixturePhone);
  await fixture.api.storageState({ path: FIXTURE_HOST_STATE });
  await fixture.api.dispose();
  const banned = await signInByApi(banPhone);
  await banned.api.dispose();

  const run: RunState = {
    runId: Date.now().toString(36),
    uiHost: spaced(uiHost),
    wrongCodePhone: spaced(wrongCodePhone).input,
    fixtureHost: { phone: fixturePhone, id: fixture.me.id },
    banHost: { phone: banPhone, id: banned.me.id },
  };
  writeFileSync(RUN_STATE, JSON.stringify(run, null, 2));
}
