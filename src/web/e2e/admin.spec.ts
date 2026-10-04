import { Page, expect, test } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { ADMIN_STORAGE_STATE } from './playwright.config';
import {
  hostApi,
  publicEventStatus,
  publishedScrimmage,
  reportFrom,
  startSignIn,
  uniqueTitle,
  verifySignIn,
} from './api-fixtures';
import { guardConsole } from './console-guard';
import { FIXTURE_HOST_STATE, REPORT_STATE, randomTestNetIp, readRunState } from './dev-stack';

test.use({ storageState: ADMIN_STORAGE_STATE });

function reportedEvent(): { publicId: string; title: string } {
  return JSON.parse(readFileSync(REPORT_STATE, 'utf8')) as { publicId: string; title: string };
}

/** Every admin action asks for an audit reason in the same dialog. */
async function confirmWithReason(page: Page, title: string, confirmLabel: string): Promise<void> {
  const dialog = page.getByRole('dialog', { name: title });
  await dialog.getByLabel('Reason').fill(`e2e: ${title.toLowerCase()}`);
  await dialog.getByRole('button', { name: confirmLabel, exact: true }).click();
  await expect(dialog).toBeHidden();
}

test.describe('moderation queue', () => {
  test.describe.configure({ mode: 'serial' });

  test('lists the event hidden by reports with its report count', async ({ page }) => {
    const reported = reportedEvent();
    await page.goto('/admin/queue');

    const item = page.getByTestId(`queue-item-${reported.publicId}`);
    await expect(item.getByRole('link', { name: reported.title })).toBeVisible();
    await expect(item.getByText('hidden', { exact: true })).toBeVisible();
    await expect(item.getByText('3 unreviewed reports')).toBeVisible();
    await expect(item.getByText('Hidden reason: reports')).toBeVisible();
  });

  test('admin restores the reported event and players can see it again', async ({ page }) => {
    const console = guardConsole(page);
    const reported = reportedEvent();
    await page.goto('/admin/queue');
    const item = page.getByTestId(`queue-item-${reported.publicId}`);

    await item.getByRole('button', { name: 'Restore' }).click();
    await confirmWithReason(page, 'Restore event', 'Restore');

    await expect(page.getByText('Event restored.')).toBeVisible();
    expect(await publicEventStatus(reported.publicId)).toBe(200);
    console.assertClean();
  });

  test('admin hides a published event that has one report', async ({ page }) => {
    const host = await hostApi(FIXTURE_HOST_STATE);
    const flagged = await publishedScrimmage(host, { title: uniqueTitle('Flagged Once') });
    await host.dispose();
    expect((await reportFrom(randomTestNetIp(), flagged.publicId, 'inaccurate')).ok()).toBe(true);
    await page.goto('/admin/queue');
    const item = page.getByTestId(`queue-item-${flagged.publicId}`);
    await expect(item.getByText('1 unreviewed report', { exact: true })).toBeVisible();

    await item.getByRole('button', { name: 'Hide' }).click();
    await confirmWithReason(page, 'Hide event', 'Hide');

    await expect(page.getByText('Event hidden.')).toBeVisible();
    await expect(item.getByText('Hidden reason: admin')).toBeVisible();
    expect(await publicEventStatus(flagged.publicId)).toBe(404);
  });
});

test.describe('hosts', () => {
  test.describe.configure({ mode: 'serial' });

  test('admin bans a host, which blocks their sign-in', async ({ page }) => {
    const { banHost } = readRunState();
    // Start sends no code to a banned phone, so send one first; verify then refuses the banned account.
    const pendingSignIn = await startSignIn(banHost.phone);
    await page.goto(`/admin/hosts/${banHost.id}`);
    await expect(page.getByText(banHost.id)).toBeVisible();

    await page.getByRole('button', { name: 'Ban' }).click();
    await confirmWithReason(page, 'Ban host', 'Ban');

    await expect(page.getByText('Banned. 0 events hidden.')).toBeVisible();
    await expect(page.getByText('banned', { exact: true })).toBeVisible();
    const verify = await verifySignIn(pendingSignIn, banHost.phone);
    expect(verify.status()).toBe(403);
    expect(((await verify.json()) as { code: string }).code).toBe('account_banned');
    await pendingSignIn.dispose();
  });

  test('admin unbans the host', async ({ page }) => {
    const { banHost } = readRunState();
    await page.goto(`/admin/hosts/${banHost.id}`);

    await page.getByRole('button', { name: 'Unban' }).click();
    await confirmWithReason(page, 'Unban host', 'Unban');

    await expect(page.getByText('Host unbanned.')).toBeVisible();
    await expect(page.getByText('active', { exact: true })).toBeVisible();
  });
});

test('admin adds a domain to the blocklist and removes it', async ({ page }) => {
  const console = guardConsole(page);
  const domain = `e2e-${readRunState().runId}.example.net`;
  await page.goto('/admin/blocklist');
  const form = page.getByRole('form', { name: 'Add to blocklist' });

  await form.getByRole('combobox', { name: 'Kind' }).click();
  await page.getByRole('option', { name: 'Domain' }).click();
  await form.getByLabel('Value').fill(domain);
  await form.getByLabel('Reason').fill('e2e: blocklist round trip');
  await form.getByRole('button', { name: 'Add' }).click();

  await expect(page.getByText(`Blocked ${domain}.`)).toBeVisible();
  const row = page.getByRole('row').filter({ hasText: domain });
  await expect(row).toHaveCount(1);

  await row.getByRole('button', { name: `Remove ${domain}` }).click();
  await confirmWithReason(page, 'Remove from blocklist', 'Remove');

  await expect(page.getByText(`Removed ${domain}.`)).toBeVisible();
  await expect(page.getByRole('row').filter({ hasText: domain })).toHaveCount(0);
  console.assertClean();
});

test('admin creates an invite and revokes it', async ({ page }) => {
  const console = guardConsole(page);
  const line = String(100 + (Number.parseInt(readRunState().runId, 36) % 100)).padStart(4, '0');
  const display = `+1 905-555-${line}`;
  await page.goto('/admin/invites');
  const form = page.getByRole('form', { name: 'Create invite' });

  await form.getByLabel('Phone').fill(`+1 905 555 ${line}`);
  await form.getByLabel('Expires in (days)').fill('7');
  await form.getByLabel('Reason').fill('e2e: invite round trip');
  await form.getByRole('button', { name: 'Create invite' }).click();

  await expect(page.getByText('Invite created.')).toBeVisible();
  const revoke = page.getByRole('button', { name: `Revoke invite for ${display}` });
  await expect(revoke.first()).toBeVisible();
  const before = await revoke.count();

  await revoke.first().click();
  await confirmWithReason(page, 'Revoke invite', 'Revoke');

  await expect(page.getByText('Invite revoked.')).toBeVisible();
  await expect(revoke).toHaveCount(before - 1);
  console.assertClean();
});
