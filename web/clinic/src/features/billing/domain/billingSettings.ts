import { z } from 'zod';

// Mirrors BillingSettingsView in src/Modules/Billing/Medvya.Billing.Application/ICatalogue.cs (MDV-P4-002). The country's rules check the prefixes on the server.
export const paymentMethods = ['Cash', 'Card', 'UpiDirect', 'RazorpayLink'] as const;
export type PaymentMethod = (typeof paymentMethods)[number];
export type BillingSettings = {
  invoicePrefix: string; receiptPrefix: string; creditNotePrefix: string; paymentMethods: PaymentMethod[]; discountReasons: string[];
  maxDiscountPercent: number; paymentLinkExpiryMinutes: number;
  /** A refund above this (minor units) needs the practice admin (MDV-P4-006); 0: every refund does. */
  refundApprovalAboveMinor: number;
  /** The row version read; a save sends it back and is refused (409 "changed") if someone saved in between (#889). */
  version: number;
};

const whole = (min: number, max: number) => z.string().trim().regex(/^\d{1,4}$/).refine(v => Number(v) >= min && Number(v) <= max);
const prefix = z.string().trim().min(1).max(16);

export const billingSettingsSchema = z.object({
  invoicePrefix: prefix, receiptPrefix: prefix, creditNotePrefix: prefix,
  paymentMethods: z.array(z.enum(paymentMethods)).min(1),
  /** One preset reason a line. */
  discountReasons: z.string(),
  maxDiscountPercent: whole(0, 100),
  paymentLinkExpiryMinutes: whole(15, 1440),
  /** Whole rupees (or dirhams): refunds above it need the practice admin. */
  refundApprovalAbove: z.string().trim().regex(/^\d{1,7}$/),
});
export type BillingSettingsForm = z.infer<typeof billingSettingsSchema>;

export const toSettingsForm = (s: BillingSettings): BillingSettingsForm => ({
  invoicePrefix: s.invoicePrefix, receiptPrefix: s.receiptPrefix, creditNotePrefix: s.creditNotePrefix, paymentMethods: s.paymentMethods,
  discountReasons: s.discountReasons.join('\n'), maxDiscountPercent: String(s.maxDiscountPercent), paymentLinkExpiryMinutes: String(s.paymentLinkExpiryMinutes),
  refundApprovalAbove: String(Math.floor(s.refundApprovalAboveMinor / 100)),
});

export const toSettingsBody = (f: BillingSettingsForm, version: number) => ({ version,
  invoicePrefix: f.invoicePrefix.trim(), receiptPrefix: f.receiptPrefix.trim(), creditNotePrefix: f.creditNotePrefix.trim(), paymentMethods: f.paymentMethods,
  discountReasons: f.discountReasons.split('\n').map(r => r.trim()).filter(Boolean),
  maxDiscountPercent: Number(f.maxDiscountPercent), paymentLinkExpiryMinutes: Number(f.paymentLinkExpiryMinutes),
  refundApprovalAboveMinor: Number(f.refundApprovalAbove) * 100,
});
