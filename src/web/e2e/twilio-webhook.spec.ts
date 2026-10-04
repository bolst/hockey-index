import { APIRequestContext, expect, request, test } from '@playwright/test';
import { createHmac } from 'node:crypto';
import { STAGING, accessHeaders, skipUnlessStaging, stagingAdminApi } from './staging';

/**
 * POST /v1/internal/twilio-usage opens the otp_sms breaker when X-Twilio-Signature is
 * Base64(HMAC-SHA1(auth token, callback URL + form keys and values sorted by key)). Needs STAGING_TWILIO_AUTH_TOKEN;
 * STAGING_TWILIO_CALLBACK_URL defaults to `${STAGING_API_URL}/v1/internal/twilio-usage`. The breaker is closed again
 * afterwards so staging sign-in keeps working.
 */
const FORM = {
  AccountSid: 'ACe2e00000000000000000000000000000',
  UsageTriggerSid: 'UTe2e00000000000000000000000000000',
  TriggerBy: 'price',
  CurrentValue: '101.00',
  TriggerValue: '100.00',
};

function sign(authToken: string, url: string, form: Record<string, string>): string {
  const data = Object.keys(form)
    .sort()
    .reduce((text, key) => text + key + form[key], url);
  return createHmac('sha1', authToken).update(data, 'utf8').digest('base64');
}

test.describe('Twilio usage webhook', () => {
  test.describe.configure({ mode: 'serial' });
  let anonymous: APIRequestContext;
  let callbackUrl: string;

  test.beforeEach(async () => {
    skipUnlessStaging('STAGING_API_URL', 'STAGING_TWILIO_AUTH_TOKEN');
    callbackUrl = process.env['STAGING_TWILIO_CALLBACK_URL'] ?? `${STAGING.apiUrl}/v1/internal/twilio-usage`;
    anonymous = await request.newContext({ extraHTTPHeaders: accessHeaders() });
  });

  test.afterEach(async () => {
    await anonymous?.dispose();
  });

  test('rejects a tampered signature with 403', async () => {
    const signature = sign(process.env['STAGING_TWILIO_AUTH_TOKEN']!, callbackUrl, { ...FORM, CurrentValue: '1.00' });

    const response = await anonymous.post(`${STAGING.apiUrl}/v1/internal/twilio-usage`, {
      form: FORM,
      headers: { 'X-Twilio-Signature': signature },
    });

    expect(response.status()).toBe(403);
  });

  test('a correctly signed callback opens the otp_sms breaker', async () => {
    skipUnlessStaging('STAGING_ADMIN_STATE');
    const admin = await stagingAdminApi();
    try {
      const response = await anonymous.post(`${STAGING.apiUrl}/v1/internal/twilio-usage`, {
        form: FORM,
        headers: { 'X-Twilio-Signature': sign(process.env['STAGING_TWILIO_AUTH_TOKEN']!, callbackUrl, FORM) },
      });
      expect(response.status()).toBe(204);

      const breakers = (await (await admin.get(`${STAGING.apiUrl}/v1/admin/breakers`)).json()) as {
        name: string;
        isOpen: boolean;
        reason: string | null;
      }[];
      expect(breakers.find((breaker) => breaker.name === 'otp_sms')).toMatchObject({
        isOpen: true,
        reason: 'twilio_usage_trigger',
      });
    } finally {
      await admin.post(`${STAGING.apiUrl}/v1/admin/breakers/otp_sms`, {
        data: { action: 'close', reason: 'e2e: twilio webhook check done' },
      });
      await admin.dispose();
    }
  });
});
