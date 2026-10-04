import { APIRequestContext, expect, request, test } from '@playwright/test';
import { HOST_STORAGE_STATE, SEO_URL } from './playwright.config';
import { HostEvent, hostApi, publishedScrimmage, uniqueTitle } from './api-fixtures';
import { API_BASE } from './auth-helpers';
import { archiveEvents, sqlLiteral } from './dev-stack';
import { readFlowState } from './flow-state';

/**
 * Runs the Pages Functions (functions/e/[id]/[[slug]].ts, functions/sitemap.xml.ts) under `wrangler pages dev`
 * on SEO_URL, with API_ORIGIN bound to the local dev API, and reads what crawlers get: raw HTML, no JavaScript.
 */
test.use({ baseURL: SEO_URL, javaScriptEnabled: false });

let site: APIRequestContext;

test.beforeAll(async () => {
  site = await request.newContext({ baseURL: SEO_URL, maxRedirects: 0 });
});

test.afterAll(async () => {
  await site.dispose();
});

test('event page serves the title and an event summary without JavaScript', async ({ page }) => {
  const flow = readFlowState();

  const response = await page.goto(flow.publicPath);

  expect(response?.status()).toBe(200);
  await expect(page).toHaveTitle(`${flow.title} · Hockey Index`);
  const summary = page.locator('app-root article');
  await expect(summary.getByRole('heading', { level: 1 })).toHaveText(flow.title);
  await expect(summary).toContainText(', Toronto, ON');
  await expect(summary).toContainText('$15 CAD');
});

test('event page head carries description, canonical and social tags', async ({ page }) => {
  const flow = readFlowState();
  const canonical = `${SEO_URL}${flow.publicPath}`;

  await page.goto(flow.publicPath);

  const head = page.locator('head');
  await expect(head.locator('meta[name="description"]')).toHaveAttribute('content', / at .+, Toronto, ON\. Skill [0-7].*\. \$15 CAD\./);
  await expect(head.locator('link[rel="canonical"]')).toHaveAttribute('href', canonical);
  await expect(head.locator('meta[property="og:title"]')).toHaveAttribute('content', flow.title);
  await expect(head.locator('meta[property="og:url"]')).toHaveAttribute('content', canonical);
  await expect(head.locator('meta[name="twitter:card"]')).toHaveAttribute('content', 'summary');
  await expect(head.locator('meta[name="robots"]')).toHaveCount(0);
});

test('event page embeds SportsEvent JSON-LD with place and price', async ({ page }) => {
  const flow = readFlowState();

  await page.goto(flow.publicPath);

  const jsonLd = JSON.parse((await page.locator('script[type="application/ld+json"]').textContent()) ?? '{}');
  expect(jsonLd).toMatchObject({
    '@context': 'https://schema.org',
    '@type': 'SportsEvent',
    name: flow.title,
    url: `${SEO_URL}${flow.publicPath}`,
    eventStatus: 'https://schema.org/EventScheduled',
    location: {
      '@type': 'Place',
      name: expect.any(String),
      address: { '@type': 'PostalAddress', addressLocality: 'Toronto', addressRegion: 'ON', addressCountry: 'CA' },
      geo: { '@type': 'GeoCoordinates' },
    },
    offers: { '@type': 'Offer', price: '15.00', priceCurrency: 'CAD' },
  });
  expect(Date.parse(jsonLd.startDate)).not.toBeNaN();
});

test('a stale slug redirects 301 to the canonical event path', async () => {
  const flow = readFlowState();

  const response = await site.get(`/e/${flow.publicId}/old-title?ref=share`);

  expect(response.status()).toBe(301);
  expect(response.headers()['location']).toBe(`${SEO_URL}${flow.publicPath}?ref=share`);
});

test('a malformed event id returns a plain 404 marked noindex', async () => {
  const response = await site.get('/e/NOT-AN-ID/whatever');

  expect(response.status()).toBe(404);
  expect(response.headers()['x-robots-tag']).toBe('noindex');
  expect(await response.text()).toBe('Not found');
});

test('an unknown event id returns the app shell with 404 and noindex', async () => {
  const response = await site.get('/e/aaaaaaaaaa');

  expect(response.status()).toBe(404);
  expect(response.headers()['x-robots-tag']).toBe('noindex');
  expect(await response.text()).toContain('<app-root');
});

test('sitemap.xml lists the published event at its canonical path', async () => {
  const flow = readFlowState();

  const response = await site.get('/sitemap.xml');

  expect(response.status()).toBe(200);
  expect(response.headers()['content-type']).toContain('application/xml');
  const xml = await response.text();
  expect(xml).toContain('<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">');
  expect(xml).toMatch(new RegExp(`<loc>[^<]*${flow.publicPath}</loc>`));
});

test.describe('event lifecycle', () => {
  let api: APIRequestContext;

  test.beforeAll(async () => {
    api = await hostApi(HOST_STORAGE_STATE);
  });

  test.afterAll(async () => {
    await api.dispose();
  });

  test('a cancelled event is marked EventCancelled in JSON-LD and the summary', async ({ page }) => {
    const event = await publishedScrimmage(api, { title: uniqueTitle('SEO Cancelled') });
    expect((await api.post(`${API_BASE}/host/events/${event.publicId}/cancel`)).ok()).toBe(true);

    await page.goto(`/e/${event.publicId}`);

    const jsonLd = JSON.parse((await page.locator('script[type="application/ld+json"]').textContent()) ?? '{}');
    expect(jsonLd.eventStatus).toBe('https://schema.org/EventCancelled');
    await expect(page.locator('app-root article strong')).toHaveText('Cancelled');
  });

  test('an archived event page carries robots noindex', async () => {
    const event: HostEvent = await publishedScrimmage(api, { title: uniqueTitle('SEO Archived') });
    expect(archiveEvents(`public_id = ${sqlLiteral(event.publicId)}`)).toBe(1);

    const response = await site.get(`/e/${event.publicId}`, { maxRedirects: 5 });

    expect(response.status()).toBe(200);
    expect(response.headers()['x-robots-tag']).toBe('noindex');
    expect(await response.text()).toContain('<meta name="robots" content="noindex">');
  });
});
