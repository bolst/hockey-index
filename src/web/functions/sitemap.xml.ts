export interface Env {
  API_ORIGIN: string;
  EDGE_KEY?: string;
  ACCESS_CLIENT_ID?: string;
  ACCESS_CLIENT_SECRET?: string;
}

/** robots.txt points crawlers at /sitemap.xml on the site origin; the API builds it (edge-cached for an hour). */
export const onRequestGet: PagesFunction<Env> = async ({ env }) => {
  const headers = new Headers({ Accept: 'application/xml' });
  if (env.EDGE_KEY) {
    headers.set('X-HI-Edge-Key', env.EDGE_KEY);
  }
  if (env.ACCESS_CLIENT_ID && env.ACCESS_CLIENT_SECRET) {
    headers.set('CF-Access-Client-Id', env.ACCESS_CLIENT_ID);
    headers.set('CF-Access-Client-Secret', env.ACCESS_CLIENT_SECRET);
  }

  try {
    const upstream = await fetch(`${env.API_ORIGIN}/v1/sitemap.xml`, { headers });
    if (!upstream.ok) {
      return new Response('Sitemap unavailable', { status: 503, headers: { 'Cache-Control': 'no-store', 'Retry-After': '300' } });
    }
    return new Response(upstream.body, {
      status: 200,
      headers: { 'Content-Type': 'application/xml; charset=utf-8', 'Cache-Control': 'public, max-age=3600' },
    });
  } catch {
    return new Response('Sitemap unavailable', { status: 503, headers: { 'Cache-Control': 'no-store', 'Retry-After': '300' } });
  }
};
