import { BrowserContext, test as base } from '@playwright/test';

export * from '@playwright/test';

/** One light-grey pixel; Leaflet stretches it to the 256px tile. */
const TILE_PNG = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGN48ewhAAVpArB44nOfAAAAAElFTkSuQmCC',
  'base64',
);

/**
 * The OSM tile usage policy forbids automated use, and specs MUST NOT depend on the network,
 * so every map tile request gets a fixed local PNG instead.
 */
export async function stubMapTiles(context: BrowserContext): Promise<void> {
  await context.route('https://tile.openstreetmap.org/**', (route) =>
    route.fulfill({ status: 200, contentType: 'image/png', body: TILE_PNG }),
  );
}

/** Use this `test` in every spec that renders the discover map. Contexts made with `browser.newContext` call stubMapTiles. */
export const test = base.extend({
  context: async ({ context }, use) => {
    await stubMapTiles(context);
    await use(context);
  },
});
