import { describe, expect, it } from 'vitest';
import type { Appointment, AppointmentState } from './schedule';
import { cancelSchema, changeError, localDate, rulesFor } from './changes';

// 10:00 IST on Monday 19 Oct 2026 is 04:30Z.
const visit = (state: AppointmentState, start = '2026-10-19T04:30:00Z'): Appointment =>
  ({ id: 'a', reference: 'APT-A', patientId: 'p', patientName: 'Meera Iyer (synthetic)', visitType: 'FollowUp', state, start, end: start });
const at = (iso: string) => Date.parse(iso);
const desk = { timeZone: 'Asia/Kolkata', canBook: true, canCheckIn: true };
const allowed = (rules: ReturnType<typeof rulesFor>) => Object.entries(rules).filter(([, r]) => r.allowed).map(([k]) => k);

describe('appointment actions', () => {
  it('offers each action only from the states the server accepts', () => {
    const now = at('2026-10-19T05:00:00Z'); // 10:30 IST, after the start
    expect(allowed(rulesFor(visit('Booked'), { ...desk, now }))).toEqual(['checkIn', 'reschedule', 'cancel', 'noShow']);
    expect(allowed(rulesFor(visit('CheckedIn'), { ...desk, now }))).toEqual(['cancel']); // left before being seen
    for (const s of ['WithNurse', 'ReadyForDoctor', 'InConsult', 'Billing'] as const) expect(allowed(rulesFor(visit(s), { ...desk, now }))).toEqual([]);
    expect(rulesFor(visit('WithNurse'), { ...desk, now }).cancel).toEqual({ allowed: false, why: 'inVisit' });
    expect(rulesFor(visit('Done'), { ...desk, now }).reschedule).toEqual({ allowed: false, why: 'completed' });
    expect(rulesFor(visit('Cancelled'), { ...desk, now }).cancel).toEqual({ allowed: false, why: 'cancelled' });
    expect(rulesFor(visit('NoShow'), { ...desk, now }).noShow).toEqual({ allowed: false, why: 'noShow' });
  });

  it('marks a no-show only once the visit has started, and checks in only on its facility day', () => {
    const before = rulesFor(visit('Booked'), { ...desk, now: at('2026-10-19T04:29:00Z') });
    expect(before.noShow).toEqual({ allowed: false, why: 'notStarted' });
    expect(before.checkIn).toEqual({ allowed: true });
    // 18:20Z on the 18th is 23:50 IST on the 18th: the day before the visit.
    expect(rulesFor(visit('Booked'), { ...desk, now: at('2026-10-18T18:20:00Z') }).checkIn).toEqual({ allowed: false, why: 'notToday' });
    // 00:10 IST on the 19th is still the 18th in UTC: the facility's day decides.
    expect(rulesFor(visit('Booked'), { ...desk, now: at('2026-10-18T18:40:00Z') }).checkIn).toEqual({ allowed: true });
  });

  it('says when the role is the reason', () => {
    const rules = rulesFor(visit('Booked'), { ...desk, canBook: false, canCheckIn: false, now: at('2026-10-19T05:00:00Z') });
    expect(Object.values(rules).map(r => (r.allowed ? 'ok' : r.why))).toEqual(['noCheckIn', 'noAccess', 'noAccess', 'noAccess']);
  });

  it('takes local dates in the facility zone, IST and GST', () => {
    expect(localDate('2026-10-18T18:40:00Z', 'Asia/Kolkata')).toBe('2026-10-19');
    expect(localDate('2026-10-18T19:40:00Z', 'Asia/Dubai')).toBe('2026-10-18');
    expect(localDate('2026-10-18T20:00:00Z', 'Asia/Dubai')).toBe('2026-10-19');
  });

  it('cancels only with a chosen reason, and a note for Other', () => {
    expect(cancelSchema.safeParse({ note: '' }).success).toBe(false);
    expect(cancelSchema.safeParse({ reason: 'PatientRequest', note: '' }).success).toBe(true);
    const other = cancelSchema.safeParse({ reason: 'Other', note: '   ' });
    expect(other.success ? null : other.error.issues.map(i => [i.path.join('.'), i.message])).toEqual([['note', 'note']]);
    expect(cancelSchema.safeParse({ reason: 'PatientUnwell', note: 'x'.repeat(501) }).success).toBe(false);
  });

  it('maps unknown error codes to a generic failure', () => {
    expect([changeError('changed'), changeError('being_booked'), changeError('nope'), changeError(undefined)]).toEqual(['changed', 'being_booked', 'failed', 'failed']);
  });
});
