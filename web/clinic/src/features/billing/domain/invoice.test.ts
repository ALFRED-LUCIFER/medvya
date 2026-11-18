import { describe, expect, it } from 'vitest';
import {
  actionsFor, coverage, discountSchema, invoiceProblem, looksLikeCardNumber, maxDiscountMinor, needsApproval, paymentSchema, providerPayment, refundable, refundSchema,
  toMajor, toPaymentBody, toRefundBody, type Invoice, type Payment,
} from './invoice';

const line = (suggested = false) => ({ suggested }) as Invoice['lines'][number];
const pay = (state: Payment['state'], id: string = state): Payment =>
  ({ id, method: 'RazorpayLink', amountMinor: 100, state, reference: null, linkUrl: null, recordedAt: '2026-10-19T04:00:00Z', confirmedAt: null });

describe('invoice actions per state (screen-specs "Billing (P4)")', () => {
  it('edits and issues a draft only once every suggested line is confirmed', () => {
    expect(actionsFor({ state: 'Draft', paidMinor: 0, payments: [], lines: [line(true)] }))
      .toEqual({ edit: true, issue: false, pay: false, cancel: true, printBill: false, printReceipt: false, refund: false });
    expect(actionsFor({ state: 'Draft', paidMinor: 0, payments: [], lines: [] }).issue).toBe(false);
    expect(actionsFor({ state: 'Draft', paidMinor: 0, payments: [], lines: [line()] }).issue).toBe(true);
  });

  it('takes payments on an issued or partly paid bill, but not while a link is pending, and cancels only an unpaid one', () => {
    expect(actionsFor({ state: 'Issued', paidMinor: 0, payments: [], lines: [line()] }))
      .toEqual({ edit: false, issue: false, pay: true, cancel: true, printBill: true, printReceipt: false, refund: false });
    expect(actionsFor({ state: 'PartlyPaid', paidMinor: 100, payments: [], lines: [line()] }))
      .toEqual({ edit: false, issue: false, pay: true, cancel: false, printBill: true, printReceipt: true, refund: false });
    expect(actionsFor({ state: 'Issued', paidMinor: 0, payments: [pay('Pending')], lines: [line()] })).toMatchObject({ pay: false, cancel: false });
    expect(actionsFor({ state: 'Paid', paidMinor: 100, payments: [], lines: [line()] }))
      .toEqual({ edit: false, issue: false, pay: false, cancel: false, printBill: true, printReceipt: true, refund: true });
    expect(actionsFor({ state: 'Paid', paidMinor: 0, payments: [], lines: [line()] }).cancel).toBe(true); // a fully discounted bill
  });

  it('prints the bill once issued and its receipt once something is paid; a cancelled bill prints nothing (MDV-P4-005)', () => {
    expect(actionsFor({ state: 'Paid', paidMinor: 0, payments: [], lines: [line()] })).toMatchObject({ printBill: true, printReceipt: false }); // a ₹0 bill
    expect(actionsFor({ state: 'Cancelled', paidMinor: 0, payments: [], lines: [line()] })).toMatchObject({ printBill: false, printReceipt: false });
  });
});

describe('the payment the desk chases', () => {
  it('is the pending link, else the latest attempt when it failed', () => {
    expect(providerPayment([pay('Failed', 'a'), pay('Pending', 'b')])?.id).toBe('b');
    expect(providerPayment([pay('Succeeded', 'a'), pay('Failed', 'b')])?.id).toBe('b');
    expect(providerPayment([pay('Failed', 'a'), pay('Succeeded', 'b')])).toBeNull();
  });
});

describe('split payment coverage (P-10)', () => {
  it('says what remains, fully covered, or over-collected', () => {
    expect(coverage(50_000, 20_000)).toEqual({ kind: 'remaining', minor: 30_000 });
    expect(coverage(50_000, 50_000)).toEqual({ kind: 'covered', minor: 0 });
    expect(coverage(50_000, 50_001)).toEqual({ kind: 'over', minor: 1 });
  });
});

describe('payment entry', () => {
  it('never takes a card number as a reference', () => {
    expect(looksLikeCardNumber('4111 1111 1111 1111')).toBe(true);
    expect(looksLikeCardNumber('4111-1111-1111-1')).toBe(true);
    expect(looksLikeCardNumber('630912345678')).toBe(false); // a 12-digit UPI UTR
    expect(looksLikeCardNumber('TID 0042 APPR 7781')).toBe(false);
    const card = paymentSchema.safeParse({ method: 'Card', amount: '100', reference: '4111111111111111' });
    expect(card.success ? null : card.error.issues[0]?.message).toBe('card_number');
  });

  it('needs a reference for card and UPI, not for cash, and sends minor units', () => {
    expect(paymentSchema.safeParse({ method: 'UpiDirect', amount: '100', reference: '' }).success).toBe(false);
    expect(paymentSchema.safeParse({ method: 'Cash', amount: '0', reference: '' }).success).toBe(false);
    const cash = paymentSchema.parse({ method: 'Cash', amount: '1234.50', reference: 'ignored' });
    expect(toPaymentBody(cash)).toEqual({ method: 'Cash', amountMinor: 123_450, reference: null });
  });
});

describe('discount', () => {
  it('needs a reason unless it is zero, and shows the cap in minor units', () => {
    expect(discountSchema.safeParse({ amount: '50', reason: '' }).success).toBe(false);
    expect(discountSchema.safeParse({ amount: '0', reason: '' }).success).toBe(true);
    expect(maxDiscountMinor(123_455, 10)).toBe(12_345);
    expect(toMajor(123_450)).toBe('1234.50');
  });
});

describe('server refusals', () => {
  it('map to message keys, never the raw code', () => {
    expect(invoiceProblem('amountMinor:over_collected')).toBe('over_collected');
    expect(invoiceProblem('state:invalid')).toBe('state');
    expect(invoiceProblem('discountMinor:taxable_lines')).toBe('taxable_lines');
    expect(invoiceProblem('number:series_exhausted')).toBe('series_exhausted');
    expect(invoiceProblem('changed')).toBe('stale');
    expect(invoiceProblem('provider_not_connected')).toBe('provider_not_connected');
    expect(invoiceProblem('provider_something_new')).toBe('failed');
    expect(invoiceProblem(undefined)).toBe('failed');
  });
});

describe('refunds and credit notes (MDV-P4-006)', () => {
  const paid = (method: Payment['method'], amountMinor: number): Payment =>
    ({ id: method, method, amountMinor, state: 'Succeeded', reference: null, linkUrl: null, recordedAt: '2026-10-19T04:00:00Z', confirmedAt: null });
  const note = (method: Payment['method'], amountMinor: number) =>
    ({ id: `n-${method}`, number: 'CN/26-27/00001', amountMinor, method, reference: null, reason: 'Synthetic', createdByName: 'A. Joseph', createdAt: '2026-10-19T05:00:00Z' });

  it('offers a refund only on a paid bill; a refunded one takes no more', () => {
    expect(actionsFor({ state: 'Refunded', paidMinor: 100, payments: [], lines: [line()] })).toMatchObject({ refund: false, printBill: true, printReceipt: true });
    expect(actionsFor({ state: 'PartlyPaid', paidMinor: 100, payments: [], lines: [line()] }).refund).toBe(false);
  });

  it('gives back up to what is left in cash, and to a card or UPI at most what was paid that way', () => {
    const bill = { paidMinor: 50_000, refundedMinor: 10_000, payments: [paid('Cash', 20_000), paid('Card', 30_000)], creditNotes: [note('Cash', 10_000)] };
    expect(refundable(bill, 'Cash')).toBe(40_000);
    expect(refundable(bill, 'Card')).toBe(30_000);
    expect(refundable(bill, 'UpiDirect')).toBe(0);
    expect(refundable({ ...bill, refundedMinor: 35_000, creditNotes: [note('Cash', 10_000), note('Card', 25_000)] }, 'Card')).toBe(5_000);
  });

  it('asks for the practice admin above the threshold, unless the user has practice configuration', () => {
    const options = { paymentMethods: [], discountReasons: [], maxDiscountPercent: 10, mayExceedMaxDiscount: false, refundApprovalAboveMinor: 100_000 };
    expect(needsApproval(100_000, options)).toBe(false);
    expect(needsApproval(100_001, options)).toBe(true);
    expect(needsApproval(500_000, { ...options, mayExceedMaxDiscount: true })).toBe(false);
  });

  it('needs a reason, and a reference that is never a card number for a card or UPI refund', () => {
    expect(refundSchema.safeParse({ method: 'Cash', amount: '100', reference: '', reason: ' ' }).success).toBe(false);
    expect(refundSchema.safeParse({ method: 'Card', amount: '100', reference: '', reason: 'Synthetic' }).success).toBe(false);
    expect(refundSchema.safeParse({ method: 'Card', amount: '100', reference: '4111 1111 1111 1111', reason: 'Synthetic' }).success).toBe(false);
    expect(toRefundBody({ method: 'Cash', amount: '250.50', reference: 'ignored', reason: 'Synthetic' }))
      .toEqual({ method: 'Cash', amountMinor: 25_050, reference: null, reason: 'Synthetic' });
    expect(invoiceProblem('amountMinor:approval_required')).toBe('approval_required');
    expect(invoiceProblem('amountMinor:over_method')).toBe('over_method');
  });
});
