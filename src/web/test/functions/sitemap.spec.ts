import { afterEach, describe, expect, it, vi } from 'vitest';
import { Env, onRequestGet } from '../../functions/sitemap.xml';

const env: Env = { API_ORIGIN: 'https://api.hockeyindex.com', EDGE_KEY: 'edge-secret' };

afterEach(() => vi.unstubAllGlobals());

async function run(fetchMock: ReturnType<typeof vi.fn>): Promise<Response> {
  vi.stubGlobal('fetch', fetchMock);
  const context = { request: new Request('https://hockeyindex.com/sitemap.xml'), env, params: {} };
  return onRequestGet(context as unknown as Parameters<typeof onRequestGet>[0]);
}

describe('sitemap function', () => {
  it('proxies the API sitemap with the edge key', async () => {
    const fetchMock = vi.fn(async () => new Response('<urlset/>', { headers: { 'Content-Type': 'application/xml' } }));
    const response = await run(fetchMock);
    expect(response.status).toBe(200);
    expect(response.headers.get('Content-Type')).toContain('application/xml');
    expect(await response.text()).toBe('<urlset/>');
    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe('https://api.hockeyindex.com/v1/sitemap.xml');
    expect(new Headers(init.headers).get('X-HI-Edge-Key')).toBe('edge-secret');
  });

  it('returns 503 when the API fails', async () => {
    const response = await run(vi.fn(async () => new Response('', { status: 500 })));
    expect(response.status).toBe(503);
    expect(response.headers.get('Cache-Control')).toBe('no-store');
  });
});
