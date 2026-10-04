import { Page, expect } from '@playwright/test';

/** Collects console errors and uncaught page errors so a test can assert there were none. */
export function guardConsole(page: Page): { assertClean: () => void } {
  const problems: string[] = [];
  page.on('console', (message) => {
    if (message.type() === 'error') {
      problems.push(`console.error: ${message.text()}`);
    }
  });
  page.on('pageerror', (error) => problems.push(`pageerror: ${error.message}`));
  return {
    assertClean: () => expect(problems, problems.join('\n')).toEqual([]),
  };
}

/** Waits until web fonts (Roboto, Material Symbols) have loaded so screenshots are stable. */
export async function settle(page: Page): Promise<void> {
  await page.waitForLoadState('networkidle');
  await page.evaluate(async () => {
    await document.fonts.ready;
  });
}
