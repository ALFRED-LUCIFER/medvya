import { describe, expect, it } from 'vitest';
import {
  actionsFor, coverage, discountSchema, invoiceProblem, looksLikeCardNumber, maxDiscountMinor, paymentSchema, providerPayment, toMajor, toPaymentBody,
  type Invoice, type Payment,
} from './invoice';

const line = (suggested = false) => ({ suggested }) as Invoice['lines'][number];
const pay = (state: Payment['state'], id: string = state): Payment =>
  ({ id, method: 'RazorpayLink', amountMinor: 100, state, reference: null, linkUrl: null, recordedAt: '2026-10-19T04:00:00Z', confirmedAt: null });

describe('invoice actions per state (screen-specs "Billing (P4)")', () => {
  it('edits and issues a draft only once every suggested line is confirmed', () => {
    expect(actionsFor({ state: 'Draft', paidMinor: 0, payments: [], lines: [line(true)] })).toEqual({ edit: true, issue: false, pay: false, cancel: true });
    expect(actionsFor({ state: 'Draft', paidMinor: 0, payments: [], lines: [] }).issue).toBe(false);
    expect(actionsFor({ state: 'Draft', paidMinor: 0, payments: [], lines: [line()] }).issue).toBe(true);
  });

  it('takes payments on an issued or partly paid bill, but not while a link is pending, and cancels only an unpaid one', () => {
    expect(actionsFor({ state: 'Issued', paidMinor: 0, payments: [], lines: [line()] })).toEqual({ edit: false, issue: false, pay: true, cancel: true });
    expect(actionsFor({ state: 'PartlyPaid', paidMinor: 100, payments: [], lines: [line()] })).toEqual({ edit: false, issue: false, pay: true, cancel: false });
    expect(actionsFor({ state: 'Issued', paidMinor: 0, payments: [pay('Pending')], lines: [line()] })).toMatchObject({ pay: false, cancel: false });
    expect(actionsFor({ state: 'Paid', paidMinor: 100, payments: [], lines: [line()] })).toEqual({ edit: false, issue: false, pay: false, cancel: false });
    expect(actionsFor({ state: 'Paid', paidMinor: 0, payments: [], lines: [line()] }).cancel).toBe(true); // a fully discounted bill
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
