import { describe, expect, it } from 'vitest';
import { catalogueProblem } from './catalogue';
import { razorpaySchema, toRazorpayBody, webhookUrl } from './razorpay';

// Synthetic keys only.
describe('Razorpay keys (MDV-P4-007)', () => {
  it('accepts a test or live key id with both secrets, and refuses anything else', () => {
    const ok = { keyId: 'rzp_test_Synth0001', keySecret: 'synthetic-secret', webhookSecret: 'synthetic-hook' };
    expect(razorpaySchema.safeParse(ok).success).toBe(true);
    expect(razorpaySchema.safeParse({ ...ok, keyId: ' rzp_live_Synth0001 ' }).success).toBe(true);
    for (const keyId of ['', 'rzp_Synth0001', 'key_test_Synth0001', 'rzp_test_', 'rzp_test_has space'])
      expect(razorpaySchema.safeParse({ ...ok, keyId }).success, keyId).toBe(false);
    expect(razorpaySchema.safeParse({ ...ok, keySecret: '  ' }).success).toBe(false);
    expect(razorpaySchema.safeParse({ ...ok, webhookSecret: 'x'.repeat(257) }).success).toBe(false);
  });

  it('sends trimmed values and builds the webhook URL from this origin', () => {
    expect(toRazorpayBody({ keyId: ' rzp_test_Synth0001 ', keySecret: ' s ', webhookSecret: ' w ' })).toEqual({ keyId: 'rzp_test_Synth0001', keySecret: 's', webhookSecret: 'w' });
    expect(webhookUrl('https://in.medvya.example/', '/v1/webhooks/razorpay/abc')).toBe('https://in.medvya.example/v1/webhooks/razorpay/abc');
    expect(webhookUrl('https://in.medvya.example', null)).toBeNull();
  });

  it('puts a "not connected" refusal on the payment methods', () => {
    expect(catalogueProblem('paymentMethods:razorpay_not_connected')).toEqual({ field: 'paymentMethods', code: 'razorpay_not_connected' });
  });
});
