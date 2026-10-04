import { APIRequestContext, expect, test } from '@playwright/test';
import { PLAYER_CONTEXT } from './playwright.config';
import { HostEvent, hostApi, publishedScrimmage, uniqueTitle } from './api-fixtures';
import { guardConsole, settle } from './console-guard';
import { FIXTURE_HOST_STATE, archiveEvents, sqlLiteral } from './dev-stack';

const NEAR_CARLTON = '/?lat=43.66&lng=-79.38';

let api: APIRequestContext;

test.beforeAll(async () => {
  api = await hostApi(FIXTURE_HOST_STATE);
});

test.afterAll(async () => {
  await api.dispose();
});

test.describe('cancelled event', () => {
  test.describe.configure({ mode: 'serial' });
  let event: HostEvent;

  test.beforeAll(async () => {
    event = await publishedScrimmage(api, { title: uniqueTitle('Cancel Me') });
  });

  test('host cancels a published event from the dashboard', async ({ browser }) => {
    const context = await browser.newContext({ storageState: FIXTURE_HOST_STATE });
    const page = await context.newPage();
    const console = guardConsole(page);
    await page.goto('/host');

    await page.getByRole('button', { name: `More actions for ${event.title}` }).click();
    await page.getByRole('menuitem', { name: 'Cancel event' }).click();
    const confirm = page.getByRole('dialog', { name: 'Cancel this event?' });
    await confirm.getByRole('button', { name: 'Cancel event' }).click();

    await expect(page.getByText(`Cancelled ${event.title}.`)).toBeVisible();
    const card = page.getByTestId('event-card').filter({ hasText: event.title });
    await expect(card.getByText('Cancelled', { exact: true })).toBeVisible();
    await settle(page);
    console.assertClean();
    await context.close();
  });

  test.describe('player', () => {
    test.use(PLAYER_CONTEXT);

    test('sees the cancelled notice on the event page and no join instructions', async ({ page }) => {
      await page.goto(`/e/${event.publicId}`);

      await expect(page.getByRole('heading', { level: 1, name: event.title })).toBeVisible();
      await expect(page.getByTestId('cancelled-notice')).toContainText('Do not go to the rink.');
      await expect(page.getByRole('heading', { name: 'How to join' })).toHaveCount(0);
    });

    test('still finds the cancelled event in search, marked Cancelled', async ({ page }) => {
      await page.goto(NEAR_CARLTON);

      const result = page.getByRole('listitem').filter({ has: page.getByRole('link', { name: event.title }) });
      await expect(result.getByText('Cancelled', { exact: true })).toBeVisible();
    });
  });
});

test.describe('archived event', () => {
  test.describe.configure({ mode: 'serial' });
  let event: HostEvent;

  test.beforeAll(async () => {
    event = await publishedScrimmage(api, { title: uniqueTitle('Archive Me') });
    expect(archiveEvents(`public_id = ${sqlLiteral(event.publicId)}`)).toBe(1);
  });

  test.use(PLAYER_CONTEXT);

  test('event page says the event has ended', async ({ page }) => {
    await page.goto(`/e/${event.publicId}`);

    await expect(page.getByRole('heading', { level: 1, name: event.title })).toBeVisible();
    await expect(page.getByText('Ended', { exact: true })).toBeVisible();
    await expect(page.getByTestId('ended-notice')).toContainText('This event has ended.');
  });

  test('archived event no longer appears in search', async ({ page }) => {
    await page.goto(NEAR_CARLTON);

    await expect(page.getByRole('heading', { level: 2, name: /within 25 mi/ })).toBeVisible();
    await expect(page.getByRole('link', { name: event.title })).toHaveCount(0);
  });
});
