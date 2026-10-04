import { APIRequestContext, expect, test } from '@playwright/test';
import { PLAYER_CONTEXT } from './playwright.config';
import { HostEvent, createScrimmage, hostApi, hostEvent, publishEvent, publishedScrimmage, uniqueTitle } from './api-fixtures';
import { guardConsole, settle } from './console-guard';
import { FIXTURE_HOST_STATE } from './dev-stack';

const SAFE_REL = 'nofollow ugc noopener noreferrer';
/** The fake URL-reputation provider flags every host under malicious.test. */
const MALICIOUS_URL = 'https://pay.malicious.test/deposit';
const MARKUP = '<script>window.__e2eInjected = true</script><b>bold</b>';

test.describe.configure({ mode: 'serial' });

let api: APIRequestContext;
let linked: HostEvent;

test.beforeAll(async () => {
  api = await hostApi(FIXTURE_HOST_STATE);
  linked = await publishedScrimmage(api, {
    title: uniqueTitle('Links Shinny'),
    description: `Markup stays text: ${MARKUP}`,
    joinInstructions: 'Sign up at https://example.com/join then pay at the rink.',
  });
});

test.afterAll(async () => {
  await api.dispose();
});

test.describe('host', () => {
  test.use({ storageState: FIXTURE_HOST_STATE });

  test('publishing a draft with a malicious join link is blocked and names the link', async ({ page }) => {
    const draft = await createScrimmage(api, {
      title: uniqueTitle('Blocked Link'),
      joinInstructions: `Pay the deposit at ${MALICIOUS_URL} before the game.`,
    });
    await page.goto(`/host/events/${draft.publicId}/edit`);
    await expect(page.getByRole('heading', { level: 1, name: 'Edit event' })).toBeVisible();

    await page.getByRole('button', { name: 'Publish' }).click();

    const blocked = page.getByTestId('blocked-urls');
    await expect(blocked).toContainText('Remove or replace these links:');
    await expect(blocked.getByRole('listitem')).toHaveText([MALICIOUS_URL]);
    expect((await hostEvent(api, draft.publicId)).status).toBe('draft');
  });
});

test('publish API rejects a shortener link that expands to a malicious host', async () => {
  const draft = await createScrimmage(api, {
    title: uniqueTitle('Short Link'),
    joinInstructions: 'Details at https://bit.ly/malicious-e2e',
  });

  const response = await publishEvent(api, draft.publicId);

  expect(response.status()).toBe(422);
  const problem = (await response.json()) as { code: string; blockedUrls: string[] };
  expect(problem.code).toBe('join_instructions_blocked');
  expect(problem.blockedUrls).toContain('https://bit.ly/malicious-e2e');
});

test.describe('player', () => {
  test.use(PLAYER_CONTEXT);

  test('join link carries nofollow ugc noopener noreferrer and opens the leaving page', async ({ page, context }) => {
    const console = guardConsole(page);
    await page.goto(`/e/${linked.publicId}`);
    const join = page.getByRole('link', { name: /example\.com\/join/ });
    await expect(join).toHaveAttribute('rel', SAFE_REL);

    const [outPage] = await Promise.all([context.waitForEvent('page'), join.click()]);

    const outConsole = guardConsole(outPage);
    await expect(outPage).toHaveURL(/\/out\?/);
    await expect(outPage.getByRole('heading', { level: 1, name: 'You are leaving Hockey Index' })).toBeVisible();
    await expect(outPage.getByTestId('destination-host')).toHaveText('example.com');
    const proceed = outPage.getByRole('link', { name: 'Continue to example.com' });
    await expect(proceed).toHaveAttribute('href', 'https://example.com/join');
    await expect(proceed).toHaveAttribute('rel', SAFE_REL);
    await settle(outPage);
    outConsole.assertClean();
    console.assertClean();
  });

  test('leaving page refuses a link that is not in the event', async ({ page }) => {
    await page.goto(`/out?u=${encodeURIComponent('https://elsewhere.example.org/pay')}&e=${linked.publicId}`);

    await expect(page.getByRole('heading', { level: 1, name: 'We cannot open this link' })).toBeVisible();
    await expect(page.getByTestId('out-unrecognized')).toBeVisible();
    await expect(page.getByRole('link', { name: /Continue to/ })).toHaveCount(0);
  });

  test('markup in a description renders as text and never runs', async ({ page }) => {
    await page.goto(`/e/${linked.publicId}`);

    await expect(page.getByText(MARKUP)).toBeVisible();
    await expect(page.locator('.description b')).toHaveCount(0);
    expect(await page.evaluate(() => (window as unknown as { __e2eInjected?: boolean }).__e2eInjected)).toBeUndefined();
  });
});
