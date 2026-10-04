import { expect, request, test } from '@playwright/test';
import { STAGING, accessHeaders, skipUnlessStaging, stagingAdminApi } from './staging';

/**
 * An admin hide MUST purge the edge cache: the warmed event page and API response stop serving the event
 * within 120 seconds. Needs STAGING_TAKEDOWN_EVENT_ID, a published staging event the test may hide; it is
 * restored afterwards.
 */
const POLL_INTERVAL_MS = 5_000;
const TAKEDOWN_BUDGET_MS = 120_000;

test('admin hide removes a cached event from the edge within 120 seconds', async () => {
  skipUnlessStaging('STAGING_API_URL', 'STAGING_ADMIN_STATE', 'STAGING_TAKEDOWN_EVENT_ID');
  test.setTimeout(TAKEDOWN_BUDGET_MS + 60_000);
  const publicId = process.env['STAGING_TAKEDOWN_EVENT_ID']!;
  const anonymous = await request.newContext({ extraHTTPHeaders: accessHeaders() });
  const admin = await stagingAdminApi();
  const eventUrl = `${STAGING.apiUrl}/v1/events/${publicId}`;
  const pageUrl = `${STAGING.baseUrl}/e/${publicId}`;

  try {
    for (let warm = 0; warm < 2; warm++) {
      expect((await anonymous.get(eventUrl)).status()).toBe(200);
      expect((await anonymous.get(pageUrl)).status()).toBe(200);
    }

    const hidden = await admin.post(`${STAGING.apiUrl}/v1/admin/events/${publicId}/hide`, {
      data: { reason: 'e2e: takedown latency check' },
    });
    expect(hidden.ok(), await hidden.text()).toBe(true);

    await expect
      .poll(
        async () => [(await anonymous.get(eventUrl)).status(), (await anonymous.get(pageUrl)).status()],
        { timeout: TAKEDOWN_BUDGET_MS, intervals: [POLL_INTERVAL_MS] },
      )
      .toEqual([404, 404]);
  } finally {
    await admin.post(`${STAGING.apiUrl}/v1/admin/events/${publicId}/restore`, {
      data: { reason: 'e2e: takedown latency check done' },
    });
    await admin.dispose();
    await anonymous.dispose();
  }
});
