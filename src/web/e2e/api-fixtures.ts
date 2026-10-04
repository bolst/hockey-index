import { APIRequestContext, APIResponse, expect, request } from '@playwright/test';
import { API_BASE, FAKE_OTP } from './auth-helpers';
import { E2E_TITLE_PREFIX, randomTestNetIp } from './dev-stack';

export const SPA_ORIGIN = 'http://localhost:4200';
/** Credentialed API paths require this header (CSRF guard) and, for unsafe methods, the SPA origin. */
export const CREDENTIALED_HEADERS = { Origin: SPA_ORIGIN, 'X-HI-Requested-With': 'hockey-index' };
/** Matches the fake geocoder fixture `fake.mapbox.toronto-carlton`; POST /v1/venues reuses the venue by place id. */
const CARLTON = {
  name: 'Carlton Street Arena',
  providerPlaceId: 'fake.mapbox.toronto-carlton',
  address: '50 Carlton Street, Toronto, Ontario M5B 1J2, Canada',
  latitude: 43.66197,
  longitude: -79.38016,
};

export interface HostEvent {
  publicId: string;
  status: string;
  title: string;
}

export interface Me {
  id: string;
  phone: string;
}

async function expectOk(response: APIResponse, what: string): Promise<APIResponse> {
  expect(response.ok(), `${what}: ${response.status()} ${await response.text()}`).toBe(true);
  return response;
}

/**
 * Sends a sign-in code through POST /v1/auth/phone/start. A random CF-Connecting-IP keeps the per-IP OTP
 * limit from blocking reruns; the dev API honours the header like Cloudflare would.
 */
export async function startSignIn(phone: string): Promise<APIRequestContext> {
  const api = await request.newContext({
    extraHTTPHeaders: { ...CREDENTIALED_HEADERS, 'CF-Connecting-IP': randomTestNetIp() },
  });
  await expectOk(
    await api.post(`${API_BASE}/auth/phone/start`, { data: { phone, turnstileToken: 'dev-pass' } }),
    `start sign-in for ${phone}`,
  );
  return api;
}

export function verifySignIn(api: APIRequestContext, phone: string): Promise<APIResponse> {
  return api.post(`${API_BASE}/auth/phone/verify`, { data: { phone, code: FAKE_OTP } });
}

export async function signInByApi(phone: string): Promise<{ api: APIRequestContext; me: Me }> {
  const api = await startSignIn(phone);
  const verified = await expectOk(await verifySignIn(api, phone), `verify ${phone}`);
  return { api, me: (await verified.json()) as Me };
}

export function hostApi(storageState: string): Promise<APIRequestContext> {
  return request.newContext({ storageState, extraHTTPHeaders: CREDENTIALED_HEADERS });
}

/** Venue wall time in Toronto `days` from now, e.g. `2026-10-10T19:00:00`. */
export function torontoLocal(days: number, time: string): string {
  const date = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'America/Toronto',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(new Date(Date.now() + days * 24 * 3_600_000));
  return `${date}T${time}`;
}

export function uniqueTitle(label: string): string {
  return `${E2E_TITLE_PREFIX}${label} ${Date.now().toString(36)}`;
}

export interface ScrimmageOptions {
  title: string;
  description?: string;
  joinInstructions?: string;
  feeCents?: number | null;
  daysFromNow?: number;
}

export async function createScrimmage(api: APIRequestContext, options: ScrimmageOptions): Promise<HostEvent> {
  const venue = await expectOk(await api.post(`${API_BASE}/venues`, { data: { ...CARLTON, confirmNew: false } }), 'venue');
  const venueId = ((await venue.json()) as { publicId: string }).publicId;
  const days = options.daysFromNow ?? 2;
  const created = await expectOk(
    await api.post(`${API_BASE}/host/events`, {
      data: {
        venueId,
        type: 'scrimmage',
        title: options.title,
        description: options.description ?? 'Seeded by the e2e suite.',
        startsLocal: torontoLocal(days, '19:00:00'),
        endsLocal: torontoLocal(days, '20:30:00'),
        skill: { min: 2, max: 5 },
        feeCents: options.feeCents === undefined ? 1500 : options.feeCents,
        currency: 'CAD',
        joinInstructions: options.joinInstructions ?? 'Sign up at https://example.com/join and bring both jerseys.',
      },
    }),
    `create ${options.title}`,
  );
  return (await created.json()) as HostEvent;
}

export function publishEvent(api: APIRequestContext, publicId: string): Promise<APIResponse> {
  return api.post(`${API_BASE}/host/events/${publicId}/publish`);
}

export async function publishedScrimmage(api: APIRequestContext, options: ScrimmageOptions): Promise<HostEvent> {
  const draft = await createScrimmage(api, options);
  const published = await expectOk(await publishEvent(api, draft.publicId), `publish ${options.title}`);
  const event = (await published.json()) as HostEvent;
  expect(event.status).toBe('published');
  return event;
}

export async function hostEvent(api: APIRequestContext, publicId: string): Promise<HostEvent> {
  return (await (await expectOk(await api.get(`${API_BASE}/host/events/${publicId}`), 'host event')).json()) as HostEvent;
}

/** Anonymous GET /v1/events/{id}, as the event page and the Pages Function make it. */
export async function publicEventStatus(publicId: string): Promise<number> {
  const api = await request.newContext();
  try {
    return (await api.get(`${API_BASE}/events/${publicId}`)).status();
  } finally {
    await api.dispose();
  }
}

/** Sends a report from a distinct reporter: the API keys reporters on the client IP (CF-Connecting-IP). */
export async function reportFrom(ip: string, publicId: string, reason: string): Promise<APIResponse> {
  const api = await request.newContext({ extraHTTPHeaders: { 'CF-Connecting-IP': ip } });
  try {
    const response = await api.post(`${API_BASE}/events/${publicId}/reports`, {
      data: { reason, details: 'Sent by the e2e suite.', turnstileToken: 'dev-pass' },
    });
    await response.body();
    return response;
  } finally {
    await api.dispose();
  }
}
