import { Page, expect, test } from './fixtures';
import { mkdirSync, readFileSync } from 'node:fs';
import { ADMIN_STORAGE_STATE, FLOW_STATE, HOST_STORAGE_STATE } from './playwright.config';
import { guardConsole, settle } from './console-guard';

const SCREENSHOT_DIR = 'e2e/screenshots';
const TORONTO = { latitude: 43.66, longitude: -79.38 };

interface FlowState {
  publicId: string;
  publicPath: string;
  title: string;
}

interface Screen {
  name: string;
  path: string | ((flow: FlowState) => string);
  heading: string | ((flow: FlowState) => string);
}

function readFlow(): FlowState {
  return JSON.parse(readFileSync(FLOW_STATE, 'utf8')) as FlowState;
}

function outPath(flow: FlowState, url: string): string {
  return `/out?u=${encodeURIComponent(url)}&e=${flow.publicId}`;
}

const PUBLIC_SCREENS: Screen[] = [
  { name: 'event-detail', path: (flow) => flow.publicPath, heading: (flow) => flow.title },
  {
    name: 'out',
    path: (flow) => outPath(flow, 'https://example.com/join'),
    heading: 'You are leaving Hockey Index',
  },
  {
    name: 'out-unrecognized',
    path: (flow) => outPath(flow, 'https://not-in-the-event.example.org/'),
    heading: 'We cannot open this link',
  },
  { name: 'sign-in', path: '/host/sign-in', heading: 'Host sign in' },
  { name: 'skill-levels', path: '/skill-levels', heading: 'Skill levels' },
  { name: 'not-found', path: '/no-such-page', heading: 'Page not found' },
];

const HOST_SCREENS: Screen[] = [
  { name: 'account', path: '/host/account', heading: 'Account' },
  { name: 'dashboard', path: '/host', heading: 'Your events' },
  { name: 'editor-new', path: '/host/events/new', heading: 'New event' },
  { name: 'editor-edit', path: (flow) => `/host/events/${flow.publicId}/edit`, heading: 'Edit event' },
];

const ADMIN_SCREENS: Screen[] = ['queue', 'hosts', 'venues', 'blocklist', 'invites', 'breakers', 'audit'].map(
  (section) => ({ name: `admin-${section}`, path: `/admin/${section}`, heading: 'Admin' }),
);

mkdirSync(SCREENSHOT_DIR, { recursive: true });

function shotPath(project: string, name: string): string {
  return `${SCREENSHOT_DIR}/${project}-${name}.png`;
}

async function capture(page: Page, screen: Screen, project: string): Promise<void> {
  const flow = readFlow();
  const console = guardConsole(page);
  await page.goto(typeof screen.path === 'string' ? screen.path : screen.path(flow));
  const heading = typeof screen.heading === 'string' ? screen.heading : screen.heading(flow);
  await expect(page.getByRole('heading', { level: 1, name: heading })).toBeVisible();
  await expect(page.getByRole('progressbar')).toHaveCount(0);
  await settle(page);
  await page.screenshot({ path: shotPath(project, screen.name), fullPage: true });
  console.assertClean();
}

async function openDiscoverNearToronto(page: Page): Promise<void> {
  await page.context().grantPermissions(['geolocation']);
  await page.context().setGeolocation(TORONTO);
  await page.goto('/');
  await expect(page).toHaveURL(/\?lat=43\.66&lng=-79\.38/);
  await expect(page.getByRole('heading', { level: 2, name: /games? within 25 mi/ })).toBeVisible();
}

const isMobile = (project: string) => project.startsWith('mobile');

test.describe('public screens', () => {
  test('discover', async ({ page }, testInfo) => {
    const console = guardConsole(page);
    await openDiscoverNearToronto(page);
    if (!isMobile(testInfo.project.name)) {
      await expect(page.locator('app-discover-map img.leaflet-tile-loaded').first()).toBeVisible({
        timeout: 15_000,
      });
    }
    await settle(page);
    await page.screenshot({ path: shotPath(testInfo.project.name, 'discover'), fullPage: true });
    console.assertClean();
  });

  test('discover without location', async ({ page }, testInfo) => {
    const console = guardConsole(page);
    await page.route('**/v1/geo/ip', (route) =>
      route.fulfill({
        json: { latitude: null, longitude: null },
        headers: {
          'access-control-allow-origin': 'http://localhost:4200',
          'access-control-allow-credentials': 'true',
        },
      }),
    );
    await page.goto('/');
    await expect(page.getByRole('heading', { name: 'Where do you want to play?' })).toBeVisible();
    await settle(page);
    await page.screenshot({ path: shotPath(testInfo.project.name, 'discover-no-location'), fullPage: true });
    console.assertClean();
  });

  for (const screen of PUBLIC_SCREENS) {
    test(screen.name, async ({ page }, testInfo) => {
      await capture(page, screen, testInfo.project.name);
    });
  }

  test('report dialog', async ({ page }, testInfo) => {
    const flow = readFlow();
    const console = guardConsole(page);
    await page.goto(flow.publicPath);
    await page.getByRole('button', { name: 'Report' }).click();
    const dialog = page.getByRole('dialog', { name: 'Report this event' });
    await expect(dialog).toBeVisible();
    await settle(page);
    await page.screenshot({ path: shotPath(testInfo.project.name, 'report-dialog') });
    console.assertClean();
  });

  test('filters sheet and map view on narrow screens', async ({ page }, testInfo) => {
    test.skip(!isMobile(testInfo.project.name), 'the bottom sheet and map toggle only show below 600px');
    const console = guardConsole(page);
    await openDiscoverNearToronto(page);

    await page.getByRole('button', { name: /^Filters/ }).click();
    await expect(page.getByRole('heading', { name: 'Filters' })).toBeVisible();
    await settle(page);
    await page.screenshot({ path: shotPath(testInfo.project.name, 'filters-sheet') });
    await page.keyboard.press('Escape');
    await expect(page.getByRole('heading', { name: 'Filters' })).toBeHidden();

    await page.getByRole('radio', { name: /Map/ }).or(page.getByRole('button', { name: /Map/ })).first().click();
    await expect(page.locator('app-discover-map img.leaflet-tile-loaded').first()).toBeVisible({
      timeout: 15_000,
    });
    await settle(page);
    await page.screenshot({ path: shotPath(testInfo.project.name, 'discover-map') });
    console.assertClean();
  });
});

test.describe('host screens', () => {
  test.use({ storageState: HOST_STORAGE_STATE });

  for (const screen of HOST_SCREENS) {
    test(screen.name, async ({ page }, testInfo) => {
      await capture(page, screen, testInfo.project.name);
    });
  }
});

test.describe('admin screens', () => {
  test.use({ storageState: ADMIN_STORAGE_STATE });

  for (const screen of ADMIN_SCREENS) {
    test(screen.name, async ({ page }, testInfo) => {
      await capture(page, screen, testInfo.project.name);
    });
  }
});

test('narrow screens collapse navigation into a menu', async ({ page }, testInfo) => {
  test.skip(!isMobile(testInfo.project.name), 'menu only shows below 600px');
  await page.goto('/host/sign-in');
  await expect(page.getByRole('navigation', { name: 'Main' })).toBeHidden();
  await page.getByRole('button', { name: 'Open navigation menu' }).click();
  await expect(page.getByRole('menuitem', { name: /For hosts/ })).toBeVisible();
  await settle(page);
  await page.screenshot({ path: shotPath(testInfo.project.name, 'menu') });
});
