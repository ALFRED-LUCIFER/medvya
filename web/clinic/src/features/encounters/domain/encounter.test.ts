import { describe, expect, it } from 'vitest';
import { canConsult, startProblem } from './encounter';

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
