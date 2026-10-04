import { expect, stubMapTiles, test } from './fixtures';
import { PLAYER_CONTEXT } from './playwright.config';
import { guardConsole, settle } from './console-guard';
import { readFlowState } from './flow-state';

const NEAR_TORONTO = 'lat=43.66&lng=-79.38';

test.use(PLAYER_CONTEXT);

test('player near Toronto finds the published scrimmage and opens its details', async ({ page }) => {
  const console = guardConsole(page);
  const flow = readFlowState();

  await page.goto('/');
  await expect(page).toHaveURL(new RegExp(`\\?${NEAR_TORONTO}`));
  await expect(page.getByRole('combobox', { name: 'Location' })).toHaveAttribute('placeholder', 'Near Toronto, ON');
  await expect(page.getByRole('heading', { level: 2, name: /games? within 25 mi/ })).toBeVisible();
  await page.getByRole('link', { name: flow.title }).click();

  await expect(page).toHaveURL(new RegExp(`${flow.publicPath}$`));
  await expect(page.getByRole('heading', { level: 1, name: flow.title })).toBeVisible();
  await expect(page.getByText('50 Carlton Street').first()).toBeVisible();
  await expect(page.getByText('$15 CAD').first()).toBeVisible();
  await settle(page);
  console.assertClean();
});

test('event type filter hides a scrimmage when only leagues are wanted', async ({ page }) => {
  const flow = readFlowState();

  await page.goto(`/?${NEAR_TORONTO}&type=league`);

  await expect(page.getByRole('heading', { level: 2, name: /within 25 mi/ })).toBeVisible();
  await expect(page.getByRole('link', { name: flow.title })).toHaveCount(0);
});

test('max fee filter below the fee hides the $15 scrimmage', async ({ page }) => {
  const flow = readFlowState();

  await page.goto(`/?${NEAR_TORONTO}&maxFee=1000`);

  await expect(page.getByRole('heading', { level: 2, name: /within 25 mi/ })).toBeVisible();
  await expect(page.getByRole('link', { name: flow.title })).toHaveCount(0);
});

test('max fee filter at the fee keeps the $15 scrimmage', async ({ page }) => {
  const flow = readFlowState();

  await page.goto(`/?${NEAR_TORONTO}&maxFee=1500&type=scrimmage`);

  await expect(page.getByRole('link', { name: flow.title })).toBeVisible();
});

test('player far from Toronto does not see the scrimmage', async ({ page }) => {
  const flow = readFlowState();

  await page.goto('/?lat=47.61&lng=-122.33');

  await expect(page.getByRole('heading', { level: 2, name: /within 25 mi/ })).toBeVisible();
  await expect(page.getByRole('link', { name: flow.title })).toHaveCount(0);
});

test('player who blocks location access is asked where to play', async ({ browser }) => {
  const context = await browser.newContext({ ...PLAYER_CONTEXT, permissions: [] });
  await stubMapTiles(context);
  const page = await context.newPage();

  await page.goto('/');

  await expect(page.getByRole('heading', { name: 'Where do you want to play?' })).toBeVisible({ timeout: 15_000 });
  await context.close();
});

test('plain scrolling over the map scrolls the page; Ctrl+scroll zooms the map', async ({ page }) => {
  const tileZoom = () =>
    page
      .locator('img.leaflet-tile')
      .evaluateAll((tiles) =>
        Math.max(...tiles.map((tile) => Number(new URL((tile as HTMLImageElement).src).pathname.split('/')[1]))),
      );

  await page.goto(`/?${NEAR_TORONTO}`);
  const map = page.getByRole('region', { name: 'Map of results' });
  await expect(map.locator('img.leaflet-tile-loaded').first()).toBeVisible();
  const startZoom = await tileZoom();
  const box = await map.boundingBox();
  await page.mouse.move(box!.x + box!.width / 2, box!.y + box!.height / 2);

  await page.mouse.wheel(0, 300);
  await expect.poll(() => page.evaluate(() => window.scrollY)).toBeGreaterThan(0);
  expect(await tileZoom()).toBe(startZoom);

  await page.keyboard.down('Control');
  await page.mouse.wheel(0, -300);
  await page.keyboard.up('Control');
  await expect.poll(tileZoom).toBeGreaterThan(startZoom);
});
