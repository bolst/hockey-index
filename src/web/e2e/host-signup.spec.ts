import { expect, test } from '@playwright/test';
import { mkdirSync } from 'node:fs';
import { dirname } from 'node:path';
import { HOST_STORAGE_STATE } from './playwright.config';
import { signInByPhone } from './auth-helpers';
import { guardConsole, settle } from './console-guard';
import { readRunState } from './dev-stack';

/** Fake line-type lookup: numbers ending 0001–0004 are not mobile (landline, VoIP and similar). */
const LANDLINE = '+1 416 555 0001';

test('new host signs up with a texted code and lands on their account', async ({ page }) => {
  const console = guardConsole(page);
  const { uiHost } = readRunState();

  await signInByPhone(page, uiHost.input, '/host/account');

  await expect(page).toHaveURL(/\/host\/account$/);
  await expect(page.getByTestId('account-phone')).toHaveText(uiHost.display);
  await settle(page);
  console.assertClean();
  mkdirSync(dirname(HOST_STORAGE_STATE), { recursive: true });
  await page.context().storageState({ path: HOST_STORAGE_STATE });
});

test('sign-in rejects a wrong code and stays on the code step', async ({ page }) => {
  const { wrongCodePhone } = readRunState();
  await page.goto('/host/sign-in');
  await page.getByLabel('Mobile phone number').fill(wrongCodePhone);
  await page.getByRole('button', { name: 'Send code' }).click();
  await expect(page.getByText('Enter the code we texted to')).toBeVisible();

  await page.getByLabel('Code').fill('000000');
  await page.getByRole('button', { name: 'Sign in' }).click();

  await expect(page.getByText('The code is invalid or has expired.')).toBeVisible();
  await expect(page).toHaveURL(/\/host\/sign-in/);
});

test('sign-in refuses a number that cannot receive texts', async ({ page }) => {
  await page.goto('/host/sign-in');
  await page.getByLabel('Mobile phone number').fill(LANDLINE);
  await page.getByRole('button', { name: 'Send code' }).click();

  await expect(page.getByText('This number cannot receive text messages. Use a mobile number.')).toBeVisible();
  await expect(page.getByLabel('Code')).toHaveCount(0);
});
