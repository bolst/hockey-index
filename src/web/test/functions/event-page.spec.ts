import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Env, onRequest } from '../../functions/e/[id]/[[slug]]';
import { PublicEvent } from '../../src/shared-render/event-summary';

const SHELL =
  '<!doctype html><html lang="en"><head><meta charset="utf-8"><title>Hockey Index</title></head>' +
  '<body><app-root></app-root><script src="main.js" type="module"></script></body></html>';

const baseEvent: PublicEvent = {
  publicId: 'abcdefgh23',
  status: 'published',
  type: 'scrimmage',
  title: 'Sunday Night Skate',
  description: 'Bring both jerseys.',
  venue: {
    publicId: 'venue23456',
    name: 'Maple Arena',
    addressLine: '1 Rink Rd',
    city: 'Toronto',
    region: 'ON',
    country: 'CA',
    timeZone: 'America/Toronto',
    latitude: 43.66,
    longitude: -79.38,
  },
  rinkLabel: 'Rink B',
  startsLocal: '2026-10-10T19:00:00',
  endsLocal: '2026-10-10T20:30:00',
  startsAt: '2026-10-10T19:00:00-04:00',
  endsAt: '2026-10-10T20:30:00-04:00',
  startDate: null,
  endDate: null,
  scheduleText: null,
  skill: { min: 2, max: 4 },
  feeCents: 1500,
  currency: 'CAD',
  joinInstructions: 'Text the organizer.',
  publishedAt: '2026-10-01T12:00:00Z',
  cancelledAt: null,
  updatedAt: '2026-10-01T12:00:00Z',
};

const CANONICAL = '/e/abcdefgh23/sunday-night-skate';

const env: Env = {
  API_ORIGIN: 'https://api.hockeyindex.com',
  ASSETS: { fetch: vi.fn() } as unknown as Fetcher,
};

let fetchMock: ReturnType<typeof vi.fn>;
let assetsFetch: ReturnType<typeof vi.fn>;

beforeEach(() => {
  fetchMock = vi.fn();
  assetsFetch = vi.fn(async () => new Response(SHELL, { headers: { 'Content-Type': 'text/html' } }));
  vi.stubGlobal('fetch', fetchMock);
});

afterEach(() => vi.unstubAllGlobals());

function apiReturns(event: PublicEvent): void {
  fetchMock.mockResolvedValue(new Response(JSON.stringify(event), { headers: { 'Content-Type': 'application/json' } }));
}

async function run(
  path: string,
  options: { env?: Partial<Env>; method?: string } = {},
): Promise<Response> {
  const url = new URL(path, 'https://hockeyindex.com');
  const [, , id = '', ...slug] = url.pathname.split('/');
  const context = {
    request: new Request(url, { method: options.method ?? 'GET' }),
    env: { ...env, ASSETS: { fetch: assetsFetch }, ...options.env },
    params: slug.length ? { id, slug } : { id },
  };
  return onRequest(context as unknown as Parameters<typeof onRequest>[0]);
}

function requestHeaders(): Headers {
  const init = fetchMock.mock.calls[0][1] as RequestInit;
  return new Headers(init.headers);
}

describe('event page function', () => {
  it.each(['/e/ABCDEFGH23/x', '/e/abc/x', '/e/abcdefgh1!', '/e/abcdefgh019', '/e/abcdefgh2'])(
    'returns 404 for malformed id %s with zero subrequests',
    async (path) => {
      const response = await run(path);
      expect(response.status).toBe(404);
      expect(response.headers.get('Cache-Control')).toBe('no-store');
      expect(fetchMock).not.toHaveBeenCalled();
      expect(assetsFetch).not.toHaveBeenCalled();
    },
  );

  it('calls the API with the edge key and no Access headers by default', async () => {
    apiReturns(baseEvent);
    await run(CANONICAL, { env: { EDGE_KEY: 'edge-secret' } });
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock.mock.calls[0][0]).toBe('https://api.hockeyindex.com/v1/events/abcdefgh23');
    const headers = requestHeaders();
    expect(headers.get('X-HI-Edge-Key')).toBe('edge-secret');
    expect(headers.has('CF-Access-Client-Id')).toBe(false);
    expect(headers.has('Cookie')).toBe(false);
    expect((fetchMock.mock.calls[0][1] as RequestInit).method ?? 'GET').toBe('GET');
  });

  it('adds Access service-token headers when configured', async () => {
    apiReturns(baseEvent);
    await run(CANONICAL, { env: { EDGE_KEY: 'k', ACCESS_CLIENT_ID: 'id.access', ACCESS_CLIENT_SECRET: 'secret' } });
    const headers = requestHeaders();
    expect(headers.get('CF-Access-Client-Id')).toBe('id.access');
    expect(headers.get('CF-Access-Client-Secret')).toBe('secret');
  });

  it.each(['/e/abcdefgh23', '/e/abcdefgh23/old-title', `${CANONICAL}/extra`, `${CANONICAL}/`])(
    'redirects %s to the canonical slug with 301',
    async (path) => {
      apiReturns(baseEvent);
      const response = await run(`${path}?utm=1`);
      expect(response.status).toBe(301);
      expect(response.headers.get('Location')).toBe(`https://hockeyindex.com${CANONICAL}?utm=1`);
      expect(response.headers.get('Cache-Control')).toBe('no-store');
    },
  );

  it('renders title, meta, canonical, JSON-LD and summary inside app-root with no-store', async () => {
    apiReturns(baseEvent);
    const response = await run(CANONICAL);
    const html = await response.text();
    expect(response.status).toBe(200);
    expect(response.headers.get('Cache-Control')).toBe('no-store');
    expect(response.headers.get('Content-Type')).toContain('text/html');
    expect(response.headers.has('X-Robots-Tag')).toBe(false);
    expect(html).toContain('<title>Sunday Night Skate · Hockey Index</title>');
    expect(html).not.toContain('<title>Hockey Index</title>');
    expect(html).toContain(`<link rel="canonical" href="https://hockeyindex.com${CANONICAL}">`);
    expect(html).toContain('<meta property="og:title" content="Sunday Night Skate">');
    expect(html).toContain('<meta name="twitter:card" content="summary">');
    expect(html).toMatch(/<app-root><article><h1>Sunday Night Skate<\/h1>.*Skill 2–4.*\$15 CAD.*<\/article><\/app-root>/);
    expect(html).toContain('<script src="main.js" type="module"></script>');

    const jsonLd = JSON.parse(/<script type="application\/ld\+json">(.*?)<\/script>/.exec(html)?.[1] ?? '{}');
    expect(jsonLd['@type']).toBe('SportsEvent');
    expect(jsonLd.startDate).toBe(baseEvent.startsAt);
    expect(jsonLd.eventStatus).toBe('https://schema.org/EventScheduled');
    expect(jsonLd.location.address.addressLocality).toBe('Toronto');
    expect(jsonLd.offers.price).toBe('15.00');
  });

  it('escapes hostile event text in title, meta, JSON-LD and summary', async () => {
    const hostile = '</script><script>alert(1)</script>"\'<img src=x onerror=a>';
    apiReturns({ ...baseEvent, title: hostile, description: hostile, venue: { ...baseEvent.venue, name: hostile } });
    const response = await run('/e/abcdefgh23/script-script-alert-1-script-img-src-x-onerror-a');
    const html = await response.text();
    expect(response.status).toBe(200);
    expect(html).not.toContain('<script>alert(1)');
    expect(html).not.toContain('<img src=x');
    expect(html.match(/<\/script>/g)).toHaveLength(2);
    const jsonText = /<script type="application\/ld\+json">(.*?)<\/script>/.exec(html)?.[1] ?? '';
    expect(jsonText).not.toMatch(/[<>]/);
    expect(JSON.parse(jsonText).name).toBe(hostile);
  });

  it('marks cancelled events in JSON-LD and the summary', async () => {
    apiReturns({ ...baseEvent, status: 'cancelled', cancelledAt: '2026-10-02T12:00:00Z' });
    const html = await (await run(CANONICAL)).text();
    expect(html).toContain('"eventStatus":"https://schema.org/EventCancelled"');
    expect(html).toContain('<strong>Cancelled</strong>');
  });

  it('adds noindex for archived events', async () => {
    apiReturns({ ...baseEvent, status: 'archived' });
    const response = await run(CANONICAL);
    expect(response.status).toBe(200);
    expect(response.headers.get('X-Robots-Tag')).toBe('noindex');
    expect(await response.text()).toContain('<meta name="robots" content="noindex">');
  });

  it('passes an API 404 through as a 404 shell', async () => {
    fetchMock.mockResolvedValue(new Response('{"code":"event_not_found"}', { status: 404 }));
    const response = await run(CANONICAL);
    const html = await response.text();
    expect(response.status).toBe(404);
    expect(response.headers.get('Cache-Control')).toBe('no-store');
    expect(response.headers.get('X-Robots-Tag')).toBe('noindex');
    expect(html).toContain('<app-root></app-root>');
    expect(html).not.toContain('<article>');
  });

  it('returns 503 with the shell when the API fails', async () => {
    fetchMock.mockRejectedValue(new Error('network'));
    const response = await run(CANONICAL);
    expect(response.status).toBe(503);
    expect(response.headers.get('Retry-After')).toBe('30');
    expect(response.headers.get('Cache-Control')).toBe('no-store');
  });

  it('falls back to a built-in shell when assets are unavailable', async () => {
    apiReturns(baseEvent);
    assetsFetch.mockResolvedValue(new Response('missing', { status: 404 }));
    const html = await (await run(CANONICAL)).text();
    expect(html).toContain('<app-root><article>');
  });

  it('returns no body for HEAD', async () => {
    apiReturns(baseEvent);
    const response = await run(CANONICAL, { method: 'HEAD' });
    expect(response.status).toBe(200);
    expect(await response.text()).toBe('');
  });

  it('rejects other methods without subrequests', async () => {
    const response = await run(CANONICAL, { method: 'POST' });
    expect(response.status).toBe(405);
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
