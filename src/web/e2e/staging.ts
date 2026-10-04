import { APIRequestContext, request, test } from '@playwright/test';

/**
 * Staging-only specs read their targets from the environment and skip when STAGING_BASE_URL is unset:
 * - STAGING_BASE_URL: the site, e.g. https://staging.hockeyindex.com
 * - STAGING_API_URL: the API origin, e.g. https://api-staging.hockeyindex.com
 * - STAGING_ADMIN_STATE: a Playwright storage state holding a staging admin session
 * - CF_ACCESS_CLIENT_ID / CF_ACCESS_CLIENT_SECRET: Cloudflare Access service token, when staging is behind Access
 */
export const STAGING = {
  baseUrl: process.env['STAGING_BASE_URL']?.replace(/\/$/, ''),
  apiUrl: process.env['STAGING_API_URL']?.replace(/\/$/, ''),
  adminState: process.env['STAGING_ADMIN_STATE'],
};

export function skipUnlessStaging(...required: string[]): void {
  test.skip(!STAGING.baseUrl, 'staging only: set STAGING_BASE_URL');
  const missing = required.filter((name) => !process.env[name]);
  test.skip(missing.length > 0, `staging only: set ${missing.join(', ')}`);
}

export function accessHeaders(): Record<string, string> {
  const id = process.env['CF_ACCESS_CLIENT_ID'];
  const secret = process.env['CF_ACCESS_CLIENT_SECRET'];
  return id && secret ? { 'CF-Access-Client-Id': id, 'CF-Access-Client-Secret': secret } : {};
}

export function stagingAdminApi(): Promise<APIRequestContext> {
  return request.newContext({
    storageState: STAGING.adminState,
    extraHTTPHeaders: { ...accessHeaders(), Origin: STAGING.baseUrl!, 'X-HI-Requested-With': 'hockey-index' },
  });
}
