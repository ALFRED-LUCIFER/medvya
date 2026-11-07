import { describe, expect, it } from 'vitest';
import { emptySignUp, signUpSchema, toPrefill } from './signUp';

const filled = { ...emptySignUp('IN'), practiceName: ' Menon Clinic (synthetic) ', city: 'Chennai', fullName: 'Arjun Menon (synthetic)', email: 'owner@example.test' };

describe('create account (MDV-P1-059)', () => {
  it('a doctor becomes a Solo draft with the licence step; a manager becomes a Clinic draft without it', () => {
    expect(toPrefill(filled)).toEqual({
      profile: { fullName: 'Arjun Menon (synthetic)', mode: 'Solo' },
      clinic: { name: 'Menon Clinic (synthetic)', address: 'Chennai' },
    });
    expect(toPrefill({ ...filled, role: 'manager' }).profile?.mode).toBe('Clinic');
  });

  it('never carries the email or anything else into the draft', () => {
    expect(JSON.stringify(toPrefill(filled))).not.toContain('example.test');
  });

  it('needs every answer, and a real email', () => {
    expect(signUpSchema.safeParse(filled).success).toBe(true);
    for (const change of [{ practiceName: '  ' }, { city: '' }, { fullName: '' }, { email: 'owner' }])
      expect(signUpSchema.safeParse({ ...filled, ...change }).success, JSON.stringify(change)).toBe(false);
  });
});
