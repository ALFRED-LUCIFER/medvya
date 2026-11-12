import { describe, expect, it } from 'vitest';
import { bookingSchema, emptyBooking, pickSlot, readSlotReturn, slotAddress, slotError, withoutSlotReturn } from './booking';

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
    expect(slotError('terms_not_accepted')).toBe('terms_not_accepted');
    expect(slotError('surprise')).toBe('failed');
    expect(slotError(undefined)).toBe('failed');
  });
});

describe('back to the held slot after registering a new patient (MDV-P1-021)', () => {
  const calendar = '5e1d0001-0002-4000-8000-000000000101', patient = '5e1d0001-0004-4000-8000-000000000501';
  const choice = { calendarId: calendar, calendarName: 'Dr Arjun Menon', start: '2026-11-09T18:45:00Z', visitType: 'NewConsultation' as const };

  it('carries the slot on the facility day, without the doctor name, through registration and back', () => {
    const there = slotAddress(choice, 'Asia/Kolkata');
    expect(Object.fromEntries(there)).toEqual({ date: '2026-11-10', view: 'day', calendar, start: choice.start, visitType: 'NewConsultation' });
    const atRegistration = new URLSearchParams(there);
    atRegistration.set('mode', 'quick');
    const back = new URLSearchParams(pickSlot(atRegistration)!);
    expect(back.has('mode')).toBe(false);
    back.set('patient', patient);
    expect(readSlotReturn(back)).toEqual({ calendarId: calendar, start: choice.start, visitType: 'NewConsultation', patientId: patient });
    expect(Object.fromEntries(withoutSlotReturn(back))).toEqual({ date: '2026-11-10', view: 'day' });
  });

  it('ignores an address that names no slot or a malformed one', () => {
    expect(pickSlot(new URLSearchParams('mode=quick'))).toBeNull();
    const good = { calendar, start: choice.start, visitType: 'FollowUp', patient };
    expect(readSlotReturn(new URLSearchParams(good))?.visitType).toBe('NewConsultation'); // UX-10: a first visit is a new consultation
    expect(readSlotReturn(new URLSearchParams({ ...good, visitType: 'Procedure' }))?.visitType).toBe('Procedure'); // a type the desk chose stays
    for (const bad of [{ calendar: '' }, { patient: '' }, { start: 'tomorrow' }, { visitType: 'Surgery' }])
      expect(readSlotReturn(new URLSearchParams({ ...good, ...bad }))).toBeNull();
  });
});
