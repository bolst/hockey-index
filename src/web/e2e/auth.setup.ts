import { expect, test as setup } from '@playwright/test';
import { mkdirSync } from 'node:fs';
import { dirname } from 'node:path';
import { ADMIN_STORAGE_STATE } from './playwright.config';
import { hasLiveSession, signInByPhone } from './auth-helpers';
import { guardConsole, settle } from './console-guard';

/** Matches Admin__BootstrapPhones__0 in deploy/compose.dev.yaml. */
const ADMIN_PHONE = '+1 416 555 0100';

setup('admin signs in and sees the admin pages', async ({ page }) => {
  setup.skip(await hasLiveSession(ADMIN_STORAGE_STATE), 'saved admin session is still valid');
  const console = guardConsole(page);

  await signInByPhone(page, ADMIN_PHONE, '/admin');
  await expect(page).toHaveURL(/\/admin\/queue$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Admin' })).toBeVisible();
  await expect(page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: /Admin/ })).toBeVisible();
  await settle(page);
  console.assertClean();

  mkdirSync(dirname(ADMIN_STORAGE_STATE), { recursive: true });
  await page.context().storageState({ path: ADMIN_STORAGE_STATE });
});
