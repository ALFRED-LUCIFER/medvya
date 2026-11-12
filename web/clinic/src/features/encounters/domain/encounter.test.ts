import { describe, expect, it } from 'vitest';
import { canConsult, openPastDay, startProblem } from './encounter';

describe('startProblem', () => {
  it('names the open consultation so the doctor can end or leave it', () => {
    expect(startProblem(409, { code: 'open_encounter', encounterId: 'e-1' })).toEqual({ kind: 'open', encounterId: 'e-1' });
  });

  it('reads a clinical refusal, and treats an unexplained 403 as one that asking cannot fix', () => {
    expect(startProblem(403, { code: 'no_care_relationship', requestPending: true })).toEqual({ kind: 'refused', refusal: { canRequest: true, requestPending: true } });
    expect(startProblem(403, { title: 'Forbidden' })).toEqual({ kind: 'refused', refusal: { canRequest: false, requestPending: false } });
  });

  it('passes a visit conflict on by its code; anything else failed', () => {
    expect(startProblem(409, { code: 'not_arrived' })).toEqual({ kind: 'visit', code: 'not_arrived' });
    expect(startProblem(409, { code: 'invalid_transition' })).toEqual({ kind: 'visit', code: 'changed' });
    expect(startProblem(500, null)).toEqual({ kind: 'failed' });
    expect(startProblem(undefined, undefined)).toEqual({ kind: 'failed' });
  });
});

describe('canConsult', () => {
  it('is the waiting states and In consult only', () => {
    expect(['Booked', 'CheckedIn', 'WithNurse', 'ReadyForDoctor', 'InConsult', 'Billing', 'Done'].filter(canConsult)).toEqual(['CheckedIn', 'WithNurse', 'ReadyForDoctor', 'InConsult']);
  });
});

describe('openPastDay (MDV audit UX-11)', () => {
  // 20:00 on 11 Nov in Kolkata is 14:30 UTC; the facility's 12 Nov starts at 18:30 UTC on the 11th.
  const started = { status: 'Active' as const, startedAt: '2026-11-11T14:30:00Z' };
  it('flags an open consultation once the facility\'s day it started on is over, counted in the facility\'s zone', () => {
    expect(openPastDay(started, 'Asia/Kolkata', new Date('2026-11-11T18:00:00Z'))).toBe(false); // 23:30 the same evening
    expect(openPastDay(started, 'Asia/Kolkata', new Date('2026-11-11T18:31:00Z'))).toBe(true); // 00:01 the next day, still the 11th in UTC
    expect(openPastDay(started, 'UTC', new Date('2026-11-11T18:31:00Z'))).toBe(false);
  });
  it('never flags an ended consultation', () => {
    expect(openPastDay({ ...started, status: 'Ended' }, 'Asia/Kolkata', new Date('2026-11-13T00:00:00Z'))).toBe(false);
  });
});
