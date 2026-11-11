import { describe, expect, it } from 'vitest';
import { emptyWaitlistForm, offerable, toRequest, waitlistError, waitlistSchema, type WaitlistEntry } from './waitlist';

const entry = (over: Partial<WaitlistEntry>): WaitlistEntry => ({
  id: 'e1', patientId: 'p1', patientName: 'Meera Iyer (synthetic)', mrn: '000001', calendarId: null, calendarName: null, visitType: 'FollowUp', reasonCategory: 'General',
  days: [], from: null, until: null, state: 'Waiting', requestedAt: '2026-10-19T03:00:00Z', offered: null, offerExpiresAt: null, match: null, followUp: null, dueOn: null, ...over,
});

describe('waitlist form', () => {
  it('accepts no preference at all', () => expect(waitlistSchema.safeParse(emptyWaitlistForm).success).toBe(true));
  it('refuses a window that ends before it starts', () => {
    const r = waitlistSchema.safeParse({ ...emptyWaitlistForm, from: '12:00', until: '09:00' });
    expect(r.success).toBe(false);
    expect(r.error?.issues[0].path).toEqual(['until']);
    expect(waitlistSchema.safeParse({ ...emptyWaitlistForm, from: '09:00', until: '09:00' }).success).toBe(true);
  });
  it('sends no preference as null and times with seconds', () => {
    expect(toRequest('f', 'p', emptyWaitlistForm)).toMatchObject({ calendarId: null, days: [], from: null, until: null });
    expect(toRequest('f', 'p', { ...emptyWaitlistForm, calendarId: 'd', days: ['Monday', 'Wednesday'], from: '09:00', until: '11:30' })).toMatchObject({ calendarId: 'd', days: ['Monday', 'Wednesday'], from: '09:00:00', until: '11:30:00' });
  });
});

describe('offers', () => {
  const slot = { calendarId: 'd', calendarName: 'Dr', start: '2026-10-19T03:30:00Z' };
  it('only a waiting patient with a free slot can be offered one', () => {
    expect(offerable(entry({ match: slot }))).toBe(true);
    expect(offerable(entry({}))).toBe(false);
    expect(offerable(entry({ state: 'Offered', match: slot }))).toBe(false);
  });
  it('maps server codes to wording and anything else to a generic message', () => {
    expect(waitlistError('being_booked')).toBe('being_booked');
    expect(waitlistError('until:before_from')).toBe('before_from');
    expect(waitlistError('patientId:already_waiting')).toBe('already_waiting');
    expect(waitlistError('surprise')).toBe('failed');
    expect(waitlistError(undefined)).toBe('failed');
  });
});
