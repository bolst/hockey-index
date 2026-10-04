import { defineConfig, devices } from '@playwright/test';

const DEV_URL = 'http://localhost:4200';
const PAGES_URL = 'http://localhost:8788';
/** Second `wrangler pages dev` with API_ORIGIN bound to the local API, so the event page function has data. */
export const SEO_URL = 'http://localhost:8789';
export const HOST_STORAGE_STATE = 'e2e/.auth/host.json';
export const ADMIN_STORAGE_STATE = 'e2e/.auth/admin.json';
/** Written by create-publish.spec.ts: the event the host published, for the specs that depend on it. */
export const FLOW_STATE = 'e2e/.auth/flow.json';
const TORONTO = { latitude: 43.66, longitude: -79.38 };
/** A player near Toronto who allows location access. */
export const PLAYER_CONTEXT = {
  baseURL: DEV_URL,
  geolocation: TORONTO,
  permissions: ['geolocation'],
  timezoneId: 'America/Toronto',
  locale: 'en-US',
};

const common = { timezoneId: 'America/Toronto', locale: 'en-US' };
const desktop = { ...devices['Desktop Chrome'], ...common, viewport: { width: 1280, height: 800 } };
const mobile = {
  ...devices['Desktop Chrome'],
  ...common,
  viewport: { width: 360, height: 800 },
  isMobile: true,
  hasTouch: true,
};

/**
 * Three servers:
 * - `ng serve` against the docker compose dev API, for every functional spec and the screenshots;
 * - the production build behind the Pages middleware on PAGES_URL, for the CSP check. It keeps the production
 *   API_ORIGIN from wrangler.toml, because the production bundle calls that origin and the CSP must allow it;
 * - the same build on SEO_URL with API_ORIGIN bound to the local API, so seo.spec.ts gets server-rendered events.
 * global-setup.ts clears otp_log, archives earlier runs' events and signs in fresh hosts, so reruns never hit
 * the dev API's SMS or publish limits. The admin session is reused while it is valid.
 */
export default defineConfig({
  testDir: '.',
  outputDir: './test-results',
  globalSetup: './global-setup.ts',
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env['CI'],
  retries: 0,
  reporter: [['list']],
  use: {
    baseURL: DEV_URL,
    trace: 'retain-on-failure',
  },
  projects: [
    { name: 'setup', testMatch: /auth\.setup\.ts/, use: desktop },
    { name: 'host-signup', testMatch: /host-signup\.spec\.ts/, use: desktop },
    { name: 'create-publish', testMatch: /create-publish\.spec\.ts/, dependencies: ['host-signup'], use: desktop },
    { name: 'discover', testMatch: /discover\.spec\.ts/, dependencies: ['create-publish'], use: desktop },
    { name: 'links', testMatch: /links\.spec\.ts/, use: desktop },
    { name: 'skill-levels', testMatch: /skill-levels\.spec\.ts/, use: desktop },
    { name: 'skill-levels-mobile', testMatch: /skill-levels\.spec\.ts/, use: mobile },
    { name: 'cancel-archive', testMatch: /cancel-archive\.spec\.ts/, use: desktop },
    { name: 'report', testMatch: /report\.spec\.ts/, use: desktop },
    { name: 'admin', testMatch: /admin\.spec\.ts/, dependencies: ['setup', 'report'], use: desktop },
    { name: 'no-preflight', testMatch: /no-preflight\.spec\.ts/, dependencies: ['create-publish'], use: desktop },
    { name: 'seo', testMatch: /seo\.spec\.ts/, dependencies: ['create-publish'], use: { ...desktop, baseURL: SEO_URL } },
    { name: 'staging', testMatch: /(takedown|twilio-webhook)\.spec\.ts/, use: desktop },
    {
      name: 'mobile-light',
      testMatch: /screens\.spec\.ts/,
      dependencies: ['setup', 'create-publish'],
      use: { ...mobile, colorScheme: 'light' },
    },
    {
      name: 'desktop-light',
      testMatch: /screens\.spec\.ts/,
      dependencies: ['setup', 'create-publish'],
      use: { ...desktop, colorScheme: 'light' },
    },
    {
      name: 'mobile-dark',
      testMatch: /screens\.spec\.ts/,
      dependencies: ['setup', 'create-publish'],
      use: { ...mobile, colorScheme: 'dark' },
    },
    {
      name: 'desktop-dark',
      testMatch: /screens\.spec\.ts/,
      dependencies: ['setup', 'create-publish'],
      use: { ...desktop, colorScheme: 'dark' },
    },
    {
      name: 'csp-production',
      testMatch: /csp\.spec\.ts/,
      dependencies: ['create-publish'],
      use: { ...desktop, baseURL: PAGES_URL },
    },
  ],
  webServer: [
    {
      command: 'ng serve --port 4200',
      cwd: '..',
      url: DEV_URL,
      reuseExistingServer: !process.env['CI'],
      timeout: 180_000,
    },
    {
      command: 'ng build && wrangler pages dev dist/hockey-index-web/browser --port 8788 --ip 127.0.0.1',
      cwd: '..',
      url: PAGES_URL,
      reuseExistingServer: !process.env['CI'],
      timeout: 240_000,
    },
    {
      // Starts after the server above has built dist/.
      command:
        'wrangler pages dev dist/hockey-index-web/browser --port 8789 --ip 127.0.0.1 --inspector-port 9230 ' +
        '--binding API_ORIGIN=http://localhost:8080',
      cwd: '..',
      url: SEO_URL,
      reuseExistingServer: !process.env['CI'],
      timeout: 120_000,
    },
  ],
});
