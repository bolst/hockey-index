import { expect, test } from '@playwright/test';
import { writeFileSync } from 'node:fs';
import { FLOW_STATE, HOST_STORAGE_STATE } from './playwright.config';
import { guardConsole, settle } from './console-guard';
import { uniqueTitle } from './api-fixtures';

test.use({ storageState: HOST_STORAGE_STATE });

function tomorrowInToronto(): string {
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone: 'America/Toronto',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).formatToParts(new Date(Date.now() + 24 * 3_600_000));
  const part = (type: string) => parts.find((p) => p.type === type)!.value;
  return `${part('month')}/${part('day')}/${part('year')}`;
}

/**
 * The host from host-signup.spec.ts creates, publishes and duplicates a scrimmage through the editor.
 * Writes FLOW_STATE for discover, no-preflight, seo, the screenshot projects and the CSP project.
 */
test('host creates and publishes a scrimmage, then keeps a draft copy', async ({ page }) => {
  test.setTimeout(90_000);
  const console = guardConsole(page);
  const title = uniqueTitle('Friday Shinny');

  await test.step('host picks a venue and fills in a scrimmage', async () => {
    await page.goto('/host');
    await expect(page.getByTestId('limits')).toContainText('of 10 active');
    await page.getByRole('link', { name: 'New event' }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'New event' })).toBeVisible();

    await page.getByRole('radio', { name: 'Scrimmage' }).click();
    await page.getByLabel('Date', { exact: true }).fill(tomorrowInToronto());
    await page.getByLabel('Start time').fill('7:00 PM');
    await page.getByLabel('End time').fill('8:30 PM');
    await page.getByLabel('End time').press('Tab');

    await page.getByRole('combobox', { name: 'Venue' }).fill('carlton');
    await page.getByTestId('suggestions').getByRole('option').first().click();
    await page.getByLabel('Facility name').fill('Carlton Street Arena');
    await page.getByRole('button', { name: 'Add venue' }).click();
    const chosen = page.getByTestId('chosen-venue');
    const didYouMean = page.getByRole('heading', { name: 'Did you mean…?' });
    await expect(chosen.or(didYouMean)).toBeVisible();
    if (await didYouMean.isVisible()) {
      await page.getByRole('list', { name: 'Did you mean…?' }).getByRole('button').first().click();
    }
    await expect(chosen).toBeVisible();

    await page.getByRole('textbox', { name: 'Title' }).fill(title);
    await page.getByLabel('Description (optional)').fill('Friendly pickup. Bring a light and a dark jersey.');
    await page.getByRole('radio', { name: 'Fixed fee' }).check();
    await page.getByLabel('Amount').fill('15');
    await page
      .getByRole('textbox', { name: 'Join instructions' })
      .fill('Sign up at https://example.com/join then send an e-transfer to the host.');
    await expect(page.getByTestId('preview').getByRole('link', { name: /example\.com/ })).toHaveAttribute(
      'rel',
      'nofollow ugc noopener noreferrer',
    );
  });

  let publicPath = '';
  await test.step('host publishes and gets the public link', async () => {
    await page.getByRole('button', { name: 'Publish' }).click();
    await expect(page).toHaveURL(/\/host$/);
    await expect(page.getByText(`Published ${title}.`)).toBeVisible();
    const card = page.getByTestId('event-card').filter({ hasText: title });
    await expect(card.getByText('Published', { exact: true })).toBeVisible();
    await card.getByRole('button', { name: `More actions for ${title}` }).click();
    publicPath = (await page.getByRole('menuitem', { name: 'View public page' }).getAttribute('href')) ?? '';
    expect(publicPath).toMatch(/^\/e\/[a-z2-7]{10}\/e2e-friday-shinny-/);
    await page.keyboard.press('Escape');
  });

  writeFileSync(FLOW_STATE, JSON.stringify({ publicId: publicPath.split('/')[2], publicPath, title }, null, 2));

  await test.step('host keeps a draft copy for the dashboard', async () => {
    await page.getByRole('button', { name: `More actions for ${title}` }).click();
    await page.getByRole('menuitem', { name: 'Duplicate' }).click();
    await expect(page.getByText(`Created a draft copy of ${title}.`)).toBeVisible();
    await page.goto('/host');
    await expect(page.getByTestId('event-card').filter({ hasText: title }).filter({ hasText: 'Draft' })).toBeVisible();
    await settle(page);
    console.assertClean();
  });
});
