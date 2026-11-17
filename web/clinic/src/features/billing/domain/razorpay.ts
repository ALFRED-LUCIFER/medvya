import { z } from 'zod';

// Mirrors RazorpayStatus in src/Modules/Billing/Medvya.Billing.Application/Razorpay.cs (MDV-P4-007 part 2). No secret ever comes back.
export type RazorpayStatus = { connected: boolean; mode: 'Test' | 'Live' | null; keyIdEnding: string | null; webhookPath: string | null };

/** The webhook events the clinic turns on in its Razorpay dashboard: the server re-fetches the link on each of them. */
export const razorpayEvents = ['payment_link.paid', 'payment_link.expired', 'payment_link.cancelled', 'payment.failed'] as const;

const secret = z.string().trim().min(1).max(256);
// The server checks the same; the mode (test or live) is read from the key id.
export const razorpaySchema = z.object({ keyId: z.string().trim().regex(/^rzp_(test|live)_[A-Za-z0-9]{1,40}$/), keySecret: secret, webhookSecret: secret });
export type RazorpayForm = z.infer<typeof razorpaySchema>;
export const emptyRazorpayForm: RazorpayForm = { keyId: '', keySecret: '', webhookSecret: '' };

export const toRazorpayBody = (f: RazorpayForm) => ({ keyId: f.keyId.trim(), keySecret: f.keySecret.trim(), webhookSecret: f.webhookSecret.trim() });

/** The full URL to paste into Razorpay: the API is served from the same origin as this app. */
export const webhookUrl = (origin: string, path: string | null) => (path ? origin.replace(/\/$/, '') + path : null);
