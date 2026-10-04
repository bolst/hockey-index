import { CDPSession, Page, expect, request, test } from './fixtures';
import { PLAYER_CONTEXT } from './playwright.config';
import { API_BASE } from './auth-helpers';
import { SPA_ORIGIN } from './api-fixtures';
import { readFlowState } from './flow-state';

/**
 * Public reads MUST stay CORS "simple" (GET, no credentials, no custom headers) so the browser never
 * sends an OPTIONS preflight and the edge cache can serve them. Page `request` events omit preflights,
 * so the spec watches Chrome DevTools Protocol network events, which report them.
 */
test.use(PLAYER_CONTEXT);

interface Preflight {
  method: string;
  url: string;
}

async function watchPreflights(page: Page): Promise<{ preflights: Preflight[]; session: CDPSession }> {
  const preflights: Preflight[] = [];
  const session = await page.context().newCDPSession(page);
  await session.send('Network.enable');
  session.on('Network.requestWillBeSent', (event) => {
    if (event.type === 'Preflight' || event.request.method === 'OPTIONS') {
      preflights.push({ method: event.request.method, url: event.request.url });
    }
  });
  return { preflights, session };
}

test('the preflight watcher sees a preflight when a request needs one', async ({ page }) => {
  const { preflights } = await watchPreflights(page);
  await page.goto('/host/sign-in');

  await page.evaluate(async (url) => {
    await fetch(url, { headers: { 'X-E2E-Probe': '1' } }).catch(() => undefined);
  }, `${API_BASE}/search?lat=43.66&lng=-79.38`);

  await expect.poll(() => preflights.length).toBeGreaterThan(0);
});

test('discover and the event page send no preflight requests', async ({ page }) => {
  const flow = readFlowState();
  const { preflights } = await watchPreflights(page);
  const apiCalls: string[] = [];
  page.on('request', (sent) => {
    if (sent.url().startsWith(API_BASE)) {
      apiCalls.push(sent.url());
    }
  });

  await page.goto('/');
  await page.getByRole('link', { name: flow.title }).click();
  await expect(page.getByRole('heading', { level: 1, name: flow.title })).toBeVisible();
  await page.waitForLoadState('networkidle');

  expect(apiCalls.some((url) => url.includes('/search?')), 'discover called /v1/search').toBe(true);
  expect(apiCalls.some((url) => url.includes(`/events/${flow.publicId}`)), 'detail called /v1/events').toBe(true);
  expect(preflights).toEqual([]);
});

test('public API responses allow any origin and set no cookie', async () => {
  const flow = readFlowState();
  const api = await request.newContext({ extraHTTPHeaders: { Origin: SPA_ORIGIN } });
  const fromHour = `${new Date().toISOString().slice(0, 13)}:00:00Z`;
  try {
    for (const path of [`/search?lat=43.66&lng=-79.38&r=25&from=${fromHour}&days=14`, `/events/${flow.publicId}`]) {
      const response = await api.get(`${API_BASE}${path}`);
      expect(response.status(), path).toBe(200);
      expect(response.headers()['access-control-allow-origin'], path).toBe('*');
      expect(response.headers()['access-control-allow-credentials'], path).toBeUndefined();
      expect(response.headers()['set-cookie'], path).toBeUndefined();
    }
  } finally {
    await api.dispose();
  }
});
