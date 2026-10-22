import { describe, expect, it } from 'vitest';
import { bookingSchema, emptyBooking, slotError } from './booking';

describe('booking form', () => {
  it('needs a patient id and keeps the free-text reason short', () => {
    expect(bookingSchema.safeParse(emptyBooking).success).toBe(false);
    const ok = bookingSchema.safeParse({ ...emptyBooking, patientId: '5e1d0001-0004-4000-8000-000000000001', reasonText: '  ' });
    expect(ok.success && ok.data.reasonText).toBe('');
    expect(bookingSchema.safeParse({ ...emptyBooking, patientId: '5e1d0001-0004-4000-8000-000000000001', reasonText: 'x'.repeat(501) }).success).toBe(false);
  });

  it('turns API 409 codes into known errors and anything else into a generic failure', () => {
    expect(slotError('being_booked')).toBe('being_booked');
    expect(slotError('hold_expired')).toBe('hold_expired');
    expect(slotError('surprise')).toBe('failed');
    expect(slotError(undefined)).toBe('failed');
  });
});
