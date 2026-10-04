import { afterAll, beforeAll, describe, expect, it, vi } from 'vitest';
import {
  Env,
  buildContentSecurityPolicy,
  generateNonce,
  onRequest,
} from '../../functions/_middleware';

const env: Env = {
  API_ORIGIN: 'https://api.hockeyindex.com',
};

interface ElementHandler {
  element(element: { setAttribute(name: string, value: string): void }): void;
}

// Node has no HTMLRewriter; this stub applies setAttribute calls to the first opening tag of each selector.
class StubHtmlRewriter {
  private readonly handlers: [string, ElementHandler][] = [];

  on(selector: string, handler: ElementHandler): this {
    this.handlers.push([selector, handler]);
    return this;
  }

  transform(response: Response): Response {
    const handlers = this.handlers;
    const body = new ReadableStream<Uint8Array>({
      async start(controller) {
        let html = await response.text();
        for (const [selector, handler] of handlers) {
          const attributes: string[] = [];
          handler.element({ setAttribute: (name, value) => attributes.push(` ${name}="${value}"`) });
          html = html.replace(`<${selector}`, `<${selector}${attributes.join('')}`);
        }
        controller.enqueue(new TextEncoder().encode(html));
        controller.close();
      },
    });
    return new Response(body, response);
  }
}

beforeAll(() => vi.stubGlobal('HTMLRewriter', StubHtmlRewriter));
afterAll(() => vi.unstubAllGlobals());

const INDEX_HTML = '<!doctype html><html><body><app-root></app-root></body></html>';

function htmlResponse(headers: Record<string, string> = {}): Response {
  return new Response(INDEX_HTML, { headers: { 'Content-Type': 'text/html; charset=utf-8', ...headers } });
}

async function runMiddleware(
  upstream: Response,
  request = new Request('https://hockeyindex.com/'),
): Promise<{ response: Response; forwarded: Request }> {
  let forwarded = request;
  const context = {
    env,
    request,
    next: async (input?: Request) => {
      forwarded = input ?? request;
      return upstream;
    },
  } as unknown as Parameters<typeof onRequest>[0];
  const response = (await onRequest(context)) as Response;
  return { response, forwarded };
}

function styleNonceOf(csp: string | null): string | undefined {
  return csp?.match(/style-src 'self' 'nonce-([^']+)'/)?.[1];
}

describe('buildContentSecurityPolicy', () => {
  it('matches the plan policy plus a nonce-based style-src', () => {
    expect(buildContentSecurityPolicy(env, 'abc123')).toBe(
      "default-src 'self'; " +
        "script-src 'self' https://challenges.cloudflare.com; " +
        "style-src 'self' 'nonce-abc123'; " +
        "img-src 'self' data: blob: https://tile.openstreetmap.org; " +
        "connect-src 'self' https://api.hockeyindex.com; " +
        'frame-src https://challenges.cloudflare.com; ' +
        "frame-ancestors 'none'; " +
        "base-uri 'self'; " +
        "object-src 'none'",
    );
  });

  it("restricts styles to 'self' when no nonce is given", () => {
    expect(buildContentSecurityPolicy(env)).toContain("style-src 'self';");
    expect(buildContentSecurityPolicy(env)).not.toContain('unsafe-inline');
  });

  it('takes the API origin from the environment', () => {
    const csp = buildContentSecurityPolicy({ API_ORIGIN: 'https://api-staging.hockeyindex.com' });
    expect(csp).toContain("connect-src 'self' https://api-staging.hockeyindex.com;");
  });
});

describe('generateNonce', () => {
  it('returns 128 random bits as base64', () => {
    const nonce = generateNonce();
    expect(nonce).toMatch(/^[A-Za-z0-9+/]{22}==$/);
    expect(generateNonce()).not.toBe(nonce);
  });
});

describe('_middleware onRequest', () => {
  it('sets security headers and keeps the upstream status', async () => {
    const { response } = await runMiddleware(htmlResponse());
    expect(response.status).toBe(200);
    expect(response.headers.get('Content-Type')).toBe('text/html; charset=utf-8');
    expect(response.headers.get('X-Content-Type-Options')).toBe('nosniff');
    expect(response.headers.get('Referrer-Policy')).toBe('strict-origin-when-cross-origin');
    expect(response.headers.get('Permissions-Policy')).toBe('geolocation=(self)');
  });

  it('puts the same nonce in the CSP header and on <app-root ngCspNonce>', async () => {
    const { response } = await runMiddleware(htmlResponse());
    const csp = response.headers.get('Content-Security-Policy');
    const nonce = styleNonceOf(csp);
    expect(nonce).toBeTruthy();
    expect(csp).toBe(buildContentSecurityPolicy(env, nonce));
    expect(await response.text()).toContain(`<app-root ngCspNonce="${nonce}"></app-root>`);
  });

  it('uses a fresh nonce for every request', async () => {
    const first = await runMiddleware(htmlResponse());
    const second = await runMiddleware(htmlResponse());
    expect(styleNonceOf(first.response.headers.get('Content-Security-Policy'))).not.toBe(
      styleNonceOf(second.response.headers.get('Content-Security-Policy')),
    );
  });

  it('stops caching of HTML so a cached body never pairs with a new nonce', async () => {
    const { response } = await runMiddleware(
      htmlResponse({ ETag: '"abc"', 'Last-Modified': 'Wed, 01 Jan 2026 00:00:00 GMT', 'Cache-Control': 'public, max-age=0, must-revalidate' }),
    );
    expect(response.headers.get('Cache-Control')).toBe('no-store');
    expect(response.headers.get('ETag')).toBeNull();
    expect(response.headers.get('Last-Modified')).toBeNull();
  });

  it('strips conditional headers from document requests', async () => {
    const request = new Request('https://hockeyindex.com/host', {
      headers: { Accept: 'text/html', 'If-None-Match': '"abc"', 'If-Modified-Since': 'Wed, 01 Jan 2026 00:00:00 GMT' },
    });
    const { forwarded } = await runMiddleware(htmlResponse(), request);
    expect(forwarded.headers.get('If-None-Match')).toBeNull();
    expect(forwarded.headers.get('If-Modified-Since')).toBeNull();
    expect(forwarded.headers.get('Accept')).toBe('text/html');
  });

  it('leaves non-HTML responses untouched apart from security headers', async () => {
    const request = new Request('https://hockeyindex.com/main.js', { headers: { 'If-None-Match': '"js"' } });
    const { response, forwarded } = await runMiddleware(
      new Response('console.log(1)', {
        status: 200,
        headers: { 'Content-Type': 'text/javascript', ETag: '"js"', 'Cache-Control': 'public, max-age=31536000' },
      }),
      request,
    );
    expect(forwarded.headers.get('If-None-Match')).toBe('"js"');
    expect(await response.text()).toBe('console.log(1)');
    expect(response.headers.get('ETag')).toBe('"js"');
    expect(response.headers.get('Cache-Control')).toBe('public, max-age=31536000');
    expect(response.headers.get('Content-Security-Policy')).toBe(buildContentSecurityPolicy(env));
  });

  it('overrides a CSP set by an earlier handler', async () => {
    const { response } = await runMiddleware(
      new Response('x', { headers: { 'Content-Security-Policy': 'default-src *' } }),
    );
    expect(response.headers.get('Content-Security-Policy')).toBe(buildContentSecurityPolicy(env));
  });
});
