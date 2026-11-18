import { z } from 'zod';
import { toMinor, type Currency, type TaxTreatment } from './catalogue';

// Mirrors src/Modules/Billing/Medvya.Billing.Application/IInvoices.cs (MDV-P4-004; split payments from MDV-P4-009). The server enforces every rule;
// these only save a round trip and decide what the screen offers.
export type InvoiceState = 'Draft' | 'Issued' | 'PartlyPaid' | 'Paid' | 'Cancelled' | 'Refunded';
export type PaymentMethod = 'Cash' | 'Card' | 'UpiDirect' | 'RazorpayLink';
export type PaymentState = 'Pending' | 'Succeeded' | 'Failed' | 'Expired';
export type InvoiceLine = {
  id: string; serviceId: string; priceVersionId: string; description: string; sac: string | null; quantity: number; unitMinor: number;
  taxTreatment: TaxTreatment; gstRateBasisPoints: number | null; taxMinor: number; amountMinor: number; suggested: boolean;
};
export type Payment = {
  id: string; method: PaymentMethod; amountMinor: number; state: PaymentState; reference: string | null;
  /** The hosted page of a pending link, for the desk to show or copy. */
  linkUrl: string | null; recordedAt: string; confirmedAt: string | null;
};
/** Money given back on the bill and the credit note that documents it (MDV-P4-006). */
export type CreditNote = {
  id: string; number: string; amountMinor: number; method: PaymentMethod; reference: string | null; reason: string; createdByName: string; createdAt: string;
};
/** Identity only: the desk banner never shows clinical data. */
export type InvoicePatient = { id: string; fullName: string; mrn: string; dateOfBirth: string; sexAtBirth: 'Female' | 'Male' | 'Other' };
export type Invoice = {
  id: string; state: InvoiceState; number: string | null; issuedAt: string | null; patient: InvoicePatient; appointmentId: string | null; doctorId: string | null;
  currency: Currency; lines: InvoiceLine[]; subtotalMinor: number; discountMinor: number; discountReason: string | null; taxMinor: number; roundOffMinor: number;
  totalMinor: number; paidMinor: number; balanceMinor: number; payments: Payment[]; cancelReason: string | null; cancelledAt: string | null; createdAt: string;
  options: { paymentMethods: PaymentMethod[]; discountReasons: string[]; maxDiscountPercent: number; mayExceedMaxDiscount: boolean; refundApprovalAboveMinor: number };
  version: number; refundedMinor: number; creditNotes: CreditNote[];
};
export type InvoiceRow = {
  id: string; state: InvoiceState; number: string | null; patientId: string; patientName: string; mrn: string; currency: Currency;
  totalMinor: number; paidMinor: number; awaitingProvider: boolean; createdAt: string; issuedAt: string | null; refundedMinor: number;
};
export type PaymentLinkResult = { invoice: Invoice; paymentId: string; shortUrl: string };

export const invoiceStates = ['Draft', 'Issued', 'PartlyPaid', 'Paid', 'Refunded', 'Cancelled'] as const satisfies readonly InvoiceState[];
/** Methods the desk records itself; a Razorpay link is created, never typed in. */
export const deskMethods = ['Cash', 'Card', 'UpiDirect'] as const satisfies readonly PaymentMethod[];
export type DeskMethod = (typeof deskMethods)[number];

/** Each state's chip: a tone and an icon name the component maps; the words always come with them (never colour alone). */
export const stateLook: Record<InvoiceState, { tone: 'neutral' | 'info' | 'warning' | 'success' | 'danger'; icon: 'draft' | 'issued' | 'partly' | 'paid' | 'cancelled' | 'refunded' }> = {
  Draft: { tone: 'neutral', icon: 'draft' },
  Issued: { tone: 'info', icon: 'issued' },
  PartlyPaid: { tone: 'warning', icon: 'partly' },
  Paid: { tone: 'success', icon: 'paid' },
  Cancelled: { tone: 'neutral', icon: 'cancelled' },
  Refunded: { tone: 'neutral', icon: 'refunded' },
};

/** Column definitions are data; the label for each is the i18n key `billing.invoices.col.<key>`. */
export const invoiceColumns = ['number', 'patient', 'state', 'total', 'paid', 'created'] as const;
export type InvoiceColumn = (typeof invoiceColumns)[number];
export const invoiceSort = {
  number: (r: InvoiceRow) => r.number ?? '',
  patient: (r: InvoiceRow) => r.patientName,
  total: (r: InvoiceRow) => r.totalMinor,
  created: (r: InvoiceRow) => r.createdAt,
};
export type InvoiceSortColumn = keyof typeof invoiceSort;
export const isInvoiceSortable = (c: InvoiceColumn): c is InvoiceSortColumn => c in invoiceSort;

/** What the bill's state lets the desk do (docs/02-design/screen-specs.md "Billing (P4)"); the server refuses anything else. */
export function actionsFor(i: Pick<Invoice, 'state' | 'paidMinor' | 'payments' | 'lines'> & { refundedMinor?: number }) {
  const pending = i.payments.some(p => p.state === 'Pending');
  const refunded = i.refundedMinor ?? 0;
  return {
    edit: i.state === 'Draft',
    issue: i.state === 'Draft' && i.lines.length > 0 && !i.lines.some(l => l.suggested),
    // A refund ends collecting (MDV-P4-006).
    pay: (i.state === 'Issued' || i.state === 'PartlyPaid') && !pending && refunded === 0,
    // A ₹0 bill is Paid when issued with nothing paid, and a partly paid bill whose payments all went back keeps no money: both can be cancelled.
    cancel: (i.state === 'Draft' || ((i.state === 'Issued' || i.state === 'Paid' || i.state === 'PartlyPaid') && i.paidMinor === refunded)) && !pending,
    // MDV-P4-005: an issued bill prints; its receipt once something is paid. A draft or cancelled bill prints nothing.
    printBill: i.state !== 'Draft' && i.state !== 'Cancelled',
    printReceipt: i.state !== 'Draft' && i.state !== 'Cancelled' && i.paidMinor > 0,
    // MDV-P4-006: money goes back on a paid or partly paid bill, until all of it is given back (a paid bill is then Refunded).
    refund: (i.state === 'Paid' || i.state === 'PartlyPaid') && i.paidMinor > refunded,
  };
}

/** What may still be given back (paid − refunded), and the most that may go back to the card or UPI it was paid by. */
export function refundable(i: Pick<Invoice, 'paidMinor' | 'refundedMinor' | 'payments' | 'creditNotes'>, method: DeskMethod): number {
  const left = i.paidMinor - i.refundedMinor;
  if (method === 'Cash') return left; // cash may give back any of it
  const paid = i.payments.filter(p => p.state === 'Succeeded' && p.method === method).reduce((s, p) => s + p.amountMinor, 0);
  const back = i.creditNotes.filter(n => n.method === method).reduce((s, n) => s + n.amountMinor, 0);
  return Math.min(left, paid - back);
}

/** A refund that takes all the bill gives back above the clinic's threshold needs the practice admin (the server refuses it to anyone else). */
export const needsApproval = (amountMinor: number, i: Pick<Invoice, 'refundedMinor' | 'options'>) =>
  i.refundedMinor + amountMinor > i.options.refundApprovalAboveMinor && !i.options.mayExceedMaxDiscount;

/** The paper a bill or receipt prints on (MDV-P4-005): A5 for a standard printer, or the 80 mm thermal roll. */
export const printPapers = ['a5', 'thermal'] as const;
export type PrintPaper = (typeof printPapers)[number];

/** The payment the desk must chase: a pending link ("Awaiting provider confirmation") or, else, the latest failed one ("Payment failed"). */
export function providerPayment(payments: Payment[]): Payment | null {
  return payments.find(p => p.state === 'Pending') ?? (payments.at(-1)?.state === 'Failed' ? payments.at(-1)! : null);
}

/** The split-payment line under the amounts (P-10): what is left after what the desk is about to enter. */
export function coverage(balanceMinor: number, enteredMinor: number): { kind: 'remaining' | 'covered' | 'over'; minor: number } {
  const left = balanceMinor - enteredMinor;
  return left > 0 ? { kind: 'remaining', minor: left } : left === 0 ? { kind: 'covered', minor: 0 } : { kind: 'over', minor: -left };
}

/** 13 or more digits in a row, spaces and dashes ignored: a card number, which Medvya never stores (the server refuses it too). */
export const looksLikeCardNumber = (reference: string) => /\d{13,}/.test(reference.replace(/[\s-]/g, ''));

const amount = z.string().trim().regex(/^\d{1,7}(\.\d{1,2})?$/);
export const paymentSchema = z.object({
  method: z.enum(deskMethods),
  amount: amount.refine(a => toMinor(a) > 0),
  reference: z.string().trim().max(40),
}).superRefine((f, ctx) => {
  if (f.method === 'Cash') return;
  if (!f.reference) ctx.addIssue({ code: 'custom', path: ['reference'], message: 'required' });
  else if (looksLikeCardNumber(f.reference)) ctx.addIssue({ code: 'custom', path: ['reference'], message: 'card_number' });
});
export type PaymentForm = z.infer<typeof paymentSchema>;
export const toPaymentBody = (f: PaymentForm) => ({ method: f.method, amountMinor: toMinor(f.amount), reference: f.method === 'Cash' ? null : f.reference });

export const refundSchema = z.object({
  method: z.enum(deskMethods),
  amount: amount.refine(a => toMinor(a) > 0),
  reference: z.string().trim().max(40),
  reason: z.string().trim().min(1, 'required').max(200),
}).superRefine((f, ctx) => {
  if (f.method === 'Cash') return;
  if (!f.reference) ctx.addIssue({ code: 'custom', path: ['reference'], message: 'required' });
  else if (looksLikeCardNumber(f.reference)) ctx.addIssue({ code: 'custom', path: ['reference'], message: 'card_number' });
});
export type RefundForm = z.infer<typeof refundSchema>;
export const toRefundBody = (f: RefundForm) => ({ method: f.method, amountMinor: toMinor(f.amount), reference: f.method === 'Cash' ? null : f.reference, reason: f.reason });

export const discountSchema = z.object({ amount, reason: z.string().trim().max(200) })
  .refine(f => toMinor(f.amount) === 0 || f.reason.length > 0, { path: ['reason'], message: 'required' });
export type DiscountForm = z.infer<typeof discountSchema>;

/** The largest discount the user may give without practice configuration, in minor units (the server checks it again). */
export const maxDiscountMinor = (subtotalMinor: number, percent: number) => Math.floor((subtotalMinor * percent) / 100);

/** Major units in a form field ("1234.5" → "1234.50"); amounts travel as integer minor units. */
export const toMajor = (minor: number) => (minor / 100).toFixed(2);

/** A server refusal ("amountMinor:over_collected", "state:invalid", 409 "changed" → stale, "provider_not_connected") as a message key suffix. */
export type InvoiceProblem =
  | 'over_collected' | 'card_number' | 'disabled' | 'unconfirmed' | 'over_limit' | 'no_price' | 'pending' | 'invalid' | 'required' | 'too_small' | 'not_attended'
  | 'taxable_lines' | 'creating' | 'series_exhausted' | 'over_refunded' | 'over_method' | 'approval_required'
  | 'state' | 'stale' | 'provider_not_connected' | 'provider_unauthorized' | 'provider_rejected' | 'provider_unavailable' | 'failed' | 'print_failed';
const known: readonly string[] = ['over_collected', 'card_number', 'disabled', 'unconfirmed', 'over_limit', 'no_price', 'pending', 'invalid', 'required', 'too_small', 'not_attended',
  'taxable_lines', 'creating', 'series_exhausted', 'over_refunded', 'over_method', 'approval_required'];
export function invoiceProblem(code: string | undefined): InvoiceProblem {
  if (code === 'changed') return 'stale';
  if (code === 'state:invalid') return 'state';
  if (code?.startsWith('provider_')) return (['provider_not_connected', 'provider_unauthorized', 'provider_rejected', 'provider_unavailable'] as const).find(c => c === code) ?? 'failed';
  const reason = code?.split(':')[1];
  return reason && known.includes(reason) ? (reason as InvoiceProblem) : 'failed';
}
