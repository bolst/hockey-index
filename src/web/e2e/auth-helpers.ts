import { APIRequestContext, Page, expect, request } from '@playwright/test';
import { existsSync } from 'node:fs';

export const API_BASE = 'http://localhost:8080/v1';
export const FAKE_OTP = '123456';

/** Signs in through the UI. The dev API sends at most 3 codes per phone per hour. */
export async function signInByPhone(page: Page, phone: string, returnUrl: string): Promise<void> {
  await page.goto(`/host/sign-in?returnUrl=${encodeURIComponent(returnUrl)}`);
  await expect(page.getByRole('heading', { level: 1, name: 'Host sign in' })).toBeVisible();
  await page.getByLabel('Mobile phone number').fill(phone);
  await page.getByRole('button', { name: 'Send code' }).click();
  await expect(page.getByText('Enter the code we texted to')).toBeVisible();
  await page.getByLabel('Code').fill(FAKE_OTP);
  await page.getByRole('button', { name: 'Sign in' }).click();
}

/** True when a saved storage state still holds a live session, so the run can skip an SMS. */
export async function hasLiveSession(storageState: string): Promise<boolean> {
  if (!existsSync(storageState)) {
    return false;
  }
  let context: APIRequestContext | null = null;
  try {
    context = await request.newContext({ storageState, extraHTTPHeaders: { Origin: 'http://localhost:4200', 'X-HI-Requested-With': 'hockey-index' } });
    const response = await context.get(`${API_BASE}/me`);
    return response.ok();
  } catch {
    return false;
  } finally {
    await context?.dispose();
  }
}
