import { describe, expect, it } from 'vitest';
import { toSettingsBody, toSettingsForm, billingSettingsSchema } from './billingSettings';
import { catalogueProblem, linePreview, priceFor, serviceSchema, todayIn, toPriceBody, toServiceBody, upcomingFor, type PriceVersion, type Service } from './catalogue';
import { visibleTabs } from '../../settings/domain/settings';

const v = (id: string, doctorId: string | null, amountMinor: number, effectiveFrom: string, createdAt = effectiveFrom): PriceVersion =>
  ({ id, doctorId, amountMinor, currency: 'INR', effectiveFrom, createdAt });

describe('price versions (MDV-P4-002)', () => {
  const versions = [
    v('default-old', null, 50_000, '2026-10-01T00:00:00Z'),
    v('doctor', 'dr-1', 70_000, '2026-10-05T00:00:00Z'),
    v('default-next', null, 90_000, '2026-10-23T18:30:00Z'),
  ];
  const now = new Date('2026-10-19T03:00:00Z');

  it('uses the doctor’s own price before the clinic default, and a future version only from its day', () => {
    expect(priceFor(versions, 'dr-1', now)?.id).toBe('doctor');
    expect(priceFor(versions, 'dr-2', now)?.id).toBe('default-old');
    expect(priceFor(versions, null, new Date('2026-10-23T18:29:59Z'))?.id).toBe('default-old');
    expect(priceFor(versions, null, new Date('2026-10-23T18:30:00Z'))?.id).toBe('default-next');
    expect(priceFor([], null, now)).toBeNull();
    expect(upcomingFor(versions, null, now)?.id).toBe('default-next');
    expect(upcomingFor(versions, 'dr-1', now)).toBeNull();
  });

  it('of two versions from the same moment takes the later saved', () => {
    const same = [v('first', null, 1, '2026-10-01T00:00:00Z', '2026-10-01T00:00:00Z'), v('second', null, 2, '2026-10-01T00:00:00Z', '2026-10-01T00:00:01Z')];
    expect(priceFor(same, null, now)?.id).toBe('second');
  });

  it('sends whole rupees as paise and the facility day as the start', () => {
    expect(toPriceBody(null, { amount: '1234.5', effectiveOn: '2026-10-20' })).toEqual({ doctorId: null, amountMinor: 123_450, effectiveOn: '2026-10-20' });
    expect(todayIn('Asia/Kolkata', new Date('2026-10-18T19:00:00Z'))).toBe('2026-10-19'); // already Monday in India
  });
});

describe('services', () => {
  const base = { name: 'ECG', category: 'Test', code: '', sac: '999316', taxTreatment: 'Exempt', gstRate: '', durationMinutes: '' } as const;

  it('needs a GST rate up to 28 % only for a taxable service, and six SAC digits', () => {
    expect(serviceSchema.safeParse(base).success).toBe(true);
    expect(serviceSchema.safeParse({ ...base, taxTreatment: 'Taxable' }).success).toBe(false);
    expect(serviceSchema.safeParse({ ...base, taxTreatment: 'Taxable', gstRate: '18' }).success).toBe(true);
    expect(serviceSchema.safeParse({ ...base, taxTreatment: 'Taxable', gstRate: '28.01' }).success).toBe(false);
    expect(serviceSchema.safeParse({ ...base, sac: '99931' }).success).toBe(false);
  });

  it('sends basis points for a taxable service, none for an exempt one, and keeps the consultation kind on an edit', () => {
    expect(toServiceBody({ ...base, taxTreatment: 'Taxable', gstRate: '18', code: '' }).gstRateBasisPoints).toBe(1800);
    expect(toServiceBody({ ...base, gstRate: '18' }).gstRateBasisPoints).toBeNull();
    const consult = { consultationKind: 'New', version: 7 } as Service;
    expect(toServiceBody({ ...base }, consult)).toMatchObject({ consultationKind: 'New', version: 7, code: null });
  });

  it('previews a line with its tax', () => {
    expect(linePreview({ name: 'ECG', sac: '999316', taxTreatment: 'Exempt', gstRateBasisPoints: null }, 50_000)).toMatchObject({ taxMinor: 0, totalMinor: 50_000 });
    expect(linePreview({ name: 'Cream', sac: null, taxTreatment: 'Taxable', gstRateBasisPoints: 1800 }, 12_345)).toMatchObject({ taxMinor: 2222, totalMinor: 14_567 });
  });

  it('places a server refusal under its field', () => {
    expect(catalogueProblem('consultationKind:taken')).toEqual({ field: 'consultationKind', code: 'taken' });
    expect(catalogueProblem('changed')).toEqual({ field: null, code: 'changed' });
    expect(catalogueProblem('x:whatever')).toEqual({ field: null, code: 'failed' });
  });
});

describe('billing settings', () => {
  const settings = { invoicePrefix: 'INV', receiptPrefix: 'RCT', creditNotePrefix: 'CN', paymentMethods: ['Cash' as const], discountReasons: ['Staff family'], maxDiscountPercent: 10, paymentLinkExpiryMinutes: 60, refundApprovalAboveMinor: 100_000, version: 7 };

  it('round-trips the form and keeps the limits', () => {
    const form = toSettingsForm(settings);
    expect(toSettingsBody({ ...form, discountReasons: 'Staff family\n\n Senior citizen ' }, settings.version)).toMatchObject({ discountReasons: ['Staff family', 'Senior citizen'], maxDiscountPercent: 10, refundApprovalAboveMinor: 100_000, version: 7 });
    expect(form.refundApprovalAbove).toBe('1000'); // whole rupees on the form, minor units on the wire
    expect(billingSettingsSchema.safeParse({ ...form, refundApprovalAbove: '10.5' }).success).toBe(false);
    expect(billingSettingsSchema.safeParse(form).success).toBe(true);
    expect(billingSettingsSchema.safeParse({ ...form, paymentLinkExpiryMinutes: '14' }).success).toBe(false);
    expect(billingSettingsSchema.safeParse({ ...form, paymentMethods: [] }).success).toBe(false);
  });

  it('shows Services only while billing is on, and the Billing tab (its switch, #1430) always', () => {
    expect(visibleTabs(false).billing).toEqual(['billing']);
    expect(visibleTabs(true).billing).toEqual(['services', 'billing']);
    expect(visibleTabs(false).clinical).toEqual(['nurse', 'queue', 'medicines', 'dental']); // Dental (MDV-P8-E02): its switch, shown whatever billing is
  });
});
