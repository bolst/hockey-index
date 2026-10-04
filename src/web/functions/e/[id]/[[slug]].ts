import {
  PUBLIC_ID_PATTERN,
  PublicEvent,
  canonicalEventPath,
  documentTitle,
  escapeHtml,
  renderEventHead,
  renderEventSummary,
  toSummaryInput,
} from '../../../src/shared-render/event-summary';

export interface Env {
  API_ORIGIN: string;
  /** Moves API subrequests to the global edge rate-limit bucket. */
  EDGE_KEY?: string;
  /** Cloudflare Access service token; set on staging only. */
  ACCESS_CLIENT_ID?: string;
  ACCESS_CLIENT_SECRET?: string;
  ASSETS: Fetcher;
}

const RETRY_AFTER_SECONDS = '30';
const FALLBACK_SHELL =
  '<!doctype html><html lang="en"><head><meta charset="utf-8"><title>Hockey Index</title>' +
  '<meta name="viewport" content="width=device-width, initial-scale=1"></head><body><app-root></app-root></body></html>';

type EventFetch = { kind: 'found'; event: PublicEvent } | { kind: 'not-found' } | { kind: 'unavailable' };

function apiHeaders(env: Env): Headers {
  const headers = new Headers({ Accept: 'application/json' });
  if (env.EDGE_KEY) {
    headers.set('X-HI-Edge-Key', env.EDGE_KEY);
  }
  if (env.ACCESS_CLIENT_ID && env.ACCESS_CLIENT_SECRET) {
    headers.set('CF-Access-Client-Id', env.ACCESS_CLIENT_ID);
    headers.set('CF-Access-Client-Secret', env.ACCESS_CLIENT_SECRET);
  }
  return headers;
}

/** Plain GET without cookies so the subrequest is served from the zone cache of the API host. */
async function fetchEvent(env: Env, publicId: string): Promise<EventFetch> {
  try {
    const response = await fetch(`${env.API_ORIGIN}/v1/events/${publicId}`, { headers: apiHeaders(env) });
    if (response.status === 404) {
      return { kind: 'not-found' };
    }
    if (!response.ok) {
      return { kind: 'unavailable' };
    }
    return { kind: 'found', event: (await response.json()) as PublicEvent };
  } catch {
    return { kind: 'unavailable' };
  }
}

async function loadShell(env: Env, request: Request): Promise<string> {
  try {
    const response = await env.ASSETS.fetch(new URL('/', request.url));
    if (response.ok) {
      return await response.text();
    }
  } catch {
    // Fall through to the built-in shell.
  }
  return FALLBACK_SHELL;
}

function injectIntoShell(shell: string, title: string, head: string, summary: string): string {
  const titleTag = `<title>${escapeHtml(title)}</title>`;
  let html = /<title>[\s\S]*?<\/title>/i.test(shell)
    ? shell.replace(/<title>[\s\S]*?<\/title>/i, () => titleTag)
    : shell.replace(/<\/head>/i, () => `${titleTag}</head>`);
  html = html.replace(/<\/head>/i, () => `${head}</head>`);
  return html.replace(/<app-root\b[^>]*>/i, (openTag) => `${openTag}${summary}`);
}

function htmlResponse(request: Request, html: string, status: number, extraHeaders: Record<string, string> = {}): Response {
  const headers = new Headers({
    'Content-Type': 'text/html; charset=utf-8',
    'Cache-Control': 'no-store',
    ...extraHeaders,
  });
  return new Response(request.method === 'HEAD' ? null : html, { status, headers });
}

export const onRequest: PagesFunction<Env, 'id' | 'slug'> = async ({ request, env, params }) => {
  if (request.method !== 'GET' && request.method !== 'HEAD') {
    return new Response(null, { status: 405, headers: { Allow: 'GET, HEAD', 'Cache-Control': 'no-store' } });
  }

  const publicId = typeof params.id === 'string' ? params.id : '';
  if (!PUBLIC_ID_PATTERN.test(publicId)) {
    return new Response(request.method === 'HEAD' ? null : 'Not found', {
      status: 404,
      headers: { 'Content-Type': 'text/plain; charset=utf-8', 'Cache-Control': 'no-store', 'X-Robots-Tag': 'noindex' },
    });
  }

  const result = await fetchEvent(env, publicId);
  if (result.kind === 'not-found') {
    return htmlResponse(request, await loadShell(env, request), 404, { 'X-Robots-Tag': 'noindex' });
  }
  if (result.kind === 'unavailable') {
    return htmlResponse(request, await loadShell(env, request), 503, {
      'Retry-After': RETRY_AFTER_SECONDS,
      'X-Robots-Tag': 'noindex',
    });
  }

  const event = result.event;
  const url = new URL(request.url);
  const canonicalPath = canonicalEventPath(event.publicId, event.title);
  if (url.pathname !== canonicalPath) {
    return new Response(null, {
      status: 301,
      headers: { Location: `${url.origin}${canonicalPath}${url.search}`, 'Cache-Control': 'no-store' },
    });
  }

  const canonicalUrl = `${url.origin}${canonicalPath}`;
  const html = injectIntoShell(
    await loadShell(env, request),
    documentTitle(event),
    renderEventHead(event, canonicalUrl),
    renderEventSummary(toSummaryInput(event)),
  );
  return htmlResponse(request, html, 200, event.status === 'archived' ? { 'X-Robots-Tag': 'noindex' } : {});
};
