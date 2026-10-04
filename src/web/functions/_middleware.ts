export interface Env {
  API_ORIGIN: string;
}

const NONCE_BYTES = 16;
const CONDITIONAL_REQUEST_HEADERS = ['If-None-Match', 'If-Modified-Since'];

export function generateNonce(): string {
  const bytes = crypto.getRandomValues(new Uint8Array(NONCE_BYTES));
  return btoa(String.fromCharCode(...bytes));
}

export function buildContentSecurityPolicy(env: Env, styleNonce?: string): string {
  return [
    "default-src 'self'",
    "script-src 'self' https://challenges.cloudflare.com",
    styleNonce ? `style-src 'self' 'nonce-${styleNonce}'` : "style-src 'self'",
    "img-src 'self' data: blob: https://tile.openstreetmap.org",
    `connect-src 'self' ${env.API_ORIGIN}`,
    'frame-src https://challenges.cloudflare.com',
    "frame-ancestors 'none'",
    "base-uri 'self'",
    "object-src 'none'",
  ].join('; ');
}

function isDocumentRequest(request: Request): boolean {
  return (
    request.headers.get('Sec-Fetch-Dest') === 'document' ||
    (request.headers.get('Accept') ?? '').includes('text/html')
  );
}

function withoutConditionalHeaders(request: Request): Request {
  const headers = new Headers(request.headers);
  CONDITIONAL_REQUEST_HEADERS.forEach((name) => headers.delete(name));
  return new Request(request, { headers });
}

function isHtml(response: Response): boolean {
  return (response.headers.get('Content-Type') ?? '').toLowerCase().startsWith('text/html');
}

function injectAngularNonce(response: Response, nonce: string): Response {
  return new HTMLRewriter()
    .on('app-root', {
      element(element) {
        element.setAttribute('ngCspNonce', nonce);
      },
    })
    .transform(response);
}

export const onRequest: PagesFunction<Env> = async (context) => {
  const request = isDocumentRequest(context.request)
    ? withoutConditionalHeaders(context.request)
    : context.request;
  const upstream = await context.next(request);

  const response = new Response(upstream.body, upstream);
  const styleNonce = isHtml(upstream) ? generateNonce() : undefined;
  if (styleNonce) {
    response.headers.set('Cache-Control', 'no-store');
    response.headers.delete('ETag');
    response.headers.delete('Last-Modified');
  }

  response.headers.set('Content-Security-Policy', buildContentSecurityPolicy(context.env, styleNonce));
  response.headers.set('X-Content-Type-Options', 'nosniff');
  response.headers.set('Referrer-Policy', 'strict-origin-when-cross-origin');
  response.headers.set('Permissions-Policy', 'geolocation=(self)');
  return styleNonce ? injectAngularNonce(response, styleNonce) : response;
};
