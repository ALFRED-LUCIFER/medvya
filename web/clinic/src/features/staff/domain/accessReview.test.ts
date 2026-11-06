import { describe, expect, it } from 'vitest';
import { remaining, type ReviewMember } from './accessReview';

const member = (decision: ReviewMember['decision']): ReviewMember => ({ membershipId: 'm', name: 'A. Joseph (synthetic)', role: 'Receptionist', facilityName: 'Adyar', lastSignIn: null, decision });

describe('remaining', () => {
  it('counts the people without a decision', () => {
    expect(remaining({ members: [member(null), member('Confirmed'), member('Offboarded'), member(null)] })).toBe(2);
    expect(remaining({ members: [] })).toBe(0);
  });
});
