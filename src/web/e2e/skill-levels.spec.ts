import { expect, test } from './fixtures';
import { PLAYER_CONTEXT } from './playwright.config';
import { guardConsole, settle } from './console-guard';

test.use(PLAYER_CONTEXT);

test('footer link opens the skill levels guide', async ({ page }) => {
  const console = guardConsole(page);
  await page.goto('/host/sign-in');

  await page.getByRole('contentinfo').getByRole('link', { name: 'Skill levels' }).click();

  await expect(page).toHaveURL(/\/skill-levels$/);
  await expect(page).toHaveTitle('Skill levels · Hockey Index');
  await expect(page.getByRole('heading', { level: 1, name: 'Skill levels' })).toBeVisible();
  await expect(page.getByTestId('skill-level')).toHaveCount(10);
  await settle(page);
  console.assertClean();
});

test('discover skill filter links to the guide', async ({ page }, testInfo) => {
  const console = guardConsole(page);
  await page.goto('/');
  await expect(page.getByRole('heading', { level: 2, name: /games? within 25 mi/ })).toBeVisible();

  if (testInfo.project.name.includes('mobile')) {
    await page.getByRole('button', { name: /^Filters/ }).click();
  }
  await page.getByRole('link', { name: 'What do levels mean?' }).click();

  await expect(page).toHaveURL(/\/skill-levels$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Skill levels' })).toBeVisible();
  await settle(page);
  console.assertClean();
});

test('guide fits a phone screen without horizontal scroll', async ({ page }, testInfo) => {
  test.skip(!testInfo.project.name.includes('mobile'), 'phone width only');
  await page.goto('/skill-levels');
  await expect(page.getByTestId('skill-level')).toHaveCount(10);
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBe(0);
});
