import { Page, expect, test } from './fixtures';
import { readFileSync } from 'node:fs';
import { FLOW_STATE } from './playwright.config';

declare global {
  interface Window {
    __cspViolations: string[];
  }
}

interface CspCase {
  name: string;
  path: (publicId: string) => string;
  /** The event page function answers 503 with the app shell when it cannot reach the API. */
  statuses?: number[];
  ready?: (page: Page) => Promise<void>;
}

async function expectHeading(page: Page): Promise<void> {
  await expect(page.locator('h1').first()).toBeAttached({ timeout: 15_000 });
}

/** Guards fall back to sign-in with a notice because the production API is unreachable here. */
async function expectSignInFallback(page: Page): Promise<void> {
  await expect(page.getByRole('heading', { level: 1, name: 'Host sign in' })).toBeVisible({ timeout: 15_000 });
  await expect(page.getByTestId('session-unavailable')).toBeVisible();
}

/**
 * The production build behind functions/_middleware.ts (nonce CSP) MUST load without CSP violations.
 * The production API origin is unreachable here, so data-driven pages render their error or
 * not-found states; what matters is that scripts, styles, fonts and map tiles are not blocked.
 * Map tiles come from fixtures.ts, but the request still passes the CSP img-src check first.
 * Every path is a deep link, which also proves _redirects serves the app shell without looping.
 */
const CASES: CspCase[] = [
  { name: 'discover', path: () => '/' },
  {
    name: 'discover with map',
    path: () => '/?lat=43.66&lng=-79.38',
    ready: async (page) => {
      const map = page.getByRole('region', { name: 'Map of results' });
      await expect(map.locator('img.leaflet-tile-loaded').first().or(map.getByRole('status'))).toBeVisible({
        timeout: 20_000,
      });
    },
  },
  { name: 'event detail', path: (id) => `/e/${id}/e2e-friday-shinny`, statuses: [200, 503] },
  { name: 'skill levels', path: () => '/skill-levels' },
  { name: 'out', path: (id) => `/out?u=${encodeURIComponent('https://example.com/join')}&e=${id}` },
  { name: 'sign-in', path: () => '/host/sign-in' },
  { name: 'host account deep link', path: () => '/host/account', ready: expectSignInFallback },
  { name: 'admin deep link', path: () => '/admin', ready: expectSignInFallback },
];

function flowPublicId(): string {
  try {
    return (JSON.parse(readFileSync(FLOW_STATE, 'utf8')) as { publicId: string }).publicId;
  } catch {
    return 'abcdefghij';
  }
}

for (const cspCase of CASES) {
  test(`no CSP violations: ${cspCase.name}`, async ({ page }) => {
    const consoleViolations: string[] = [];
    page.on('console', (message) => {
      if (/Content Security Policy/i.test(message.text())) {
        consoleViolations.push(message.text());
      }
    });
    await page.addInitScript(() => {
      window.__cspViolations = [];
      document.addEventListener('securitypolicyviolation', (event) => {
        window.__cspViolations.push(`${event.violatedDirective} blocked ${event.blockedURI || 'inline'}`);
      });
    });

    const response = await page.goto(cspCase.path(flowPublicId()));
    expect(cspCase.statuses ?? [200]).toContain(response?.status());
    expect(response?.headers()['content-security-policy']).toMatch(/style-src 'self' 'nonce-/);
    await (cspCase.ready ?? expectHeading)(page);
    await page.waitForLoadState('networkidle');

    const fontsLoaded = await page.evaluate(async () => {
      await document.fonts.ready;
      return document.fonts.check('16px Roboto') && document.fonts.check('24px "Material Symbols Outlined"');
    });
    expect(fontsLoaded).toBe(true);
    expect(await page.evaluate(() => window.__cspViolations)).toEqual([]);
    expect(consoleViolations).toEqual([]);
  });
}
