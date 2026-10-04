import { APIRequestContext, expect, test } from '@playwright/test';
import { writeFileSync } from 'node:fs';
import { PLAYER_CONTEXT } from './playwright.config';
import { HostEvent, hostApi, publicEventStatus, publishedScrimmage, reportFrom, uniqueTitle } from './api-fixtures';
import { guardConsole, settle } from './console-guard';
import { FIXTURE_HOST_STATE, REPORT_STATE, randomTestNetIp } from './dev-stack';

/**
 * The API counts one report per reporter, keyed on the client IP, and hides an event at 3 unreviewed
 * reporters. Every request from this machine shares one socket IP, so each reporter sends its own
 * CF-Connecting-IP (honoured like Cloudflare's header) from 198.18.0.0/15, the benchmarking range.
 */
test.describe.configure({ mode: 'serial' });
test.use(PLAYER_CONTEXT);

let api: APIRequestContext;
let event: HostEvent;

test.beforeAll(async () => {
  api = await hostApi(FIXTURE_HOST_STATE);
  event = await publishedScrimmage(api, { title: uniqueTitle('Reported Shinny') });
  writeFileSync(REPORT_STATE, JSON.stringify({ publicId: event.publicId, title: event.title }, null, 2));
});

test.afterAll(async () => {
  await api.dispose();
});

test('player reports an event from its page and gets a confirmation', async ({ page }) => {
  const console = guardConsole(page);
  const reporterIp = randomTestNetIp();
  await page.route('**/v1/events/*/reports', (route) =>
    route.continue({ headers: { ...route.request().headers(), 'cf-connecting-ip': reporterIp } }),
  );
  await page.goto(`/e/${event.publicId}`);

  await page.getByRole('button', { name: 'Report' }).click();
  const dialog = page.getByRole('dialog', { name: 'Report this event' });
  await dialog.getByRole('combobox', { name: 'Reason' }).click();
  await page.getByRole('option', { name: 'Scam or asks for money up front' }).click();
  await dialog.getByLabel('Details (optional)').fill('Asks for a deposit to a personal account.');
  await dialog.getByRole('button', { name: 'Send report' }).click();

  await expect(page.getByText('Thanks. A moderator will review this event.')).toBeVisible();
  await expect(dialog).toBeHidden();
  await settle(page);
  console.assertClean();
});

test('event stays public after two distinct reporters', async () => {
  const second = await reportFrom(randomTestNetIp(), event.publicId, 'spam');

  expect(second.ok(), `second report: ${second.status()}`).toBe(true);
  expect(await publicEventStatus(event.publicId)).toBe(200);
});

test('third distinct reporter hides the event from players', async ({ page }) => {
  const third = await reportFrom(randomTestNetIp(), event.publicId, 'offensive');

  expect(third.ok(), `third report: ${third.status()}`).toBe(true);
  expect(await publicEventStatus(event.publicId)).toBe(404);
  await page.goto(`/e/${event.publicId}`);
  await expect(page.getByRole('heading', { level: 1, name: 'Event not found' })).toBeVisible();
});

test('host dashboard shows the auto-hidden event as Hidden', async ({ browser }) => {
  const context = await browser.newContext({ storageState: FIXTURE_HOST_STATE });
  const page = await context.newPage();

  await page.goto('/host');

  const card = page.getByTestId('event-card').filter({ hasText: event.title });
  await expect(card.getByText('Hidden', { exact: true })).toBeVisible();
  await context.close();
});
