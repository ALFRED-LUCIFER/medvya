import { describe, expect, it } from 'vitest';
import { inviteSchema, isAllowed } from './invitation';

const valid = { email: 'joseph@example.test', role: 'Receptionist', facilityId: '5e1d0001-0001-4000-8000-000000000001' };

describe('invite form rules', () => {
  it('accepts one email, a staff role and a facility', () => expect(inviteSchema.safeParse(valid).success).toBe(true));

  it.each([
    ['a display name instead of a bare address', { email: 'Joseph <joseph@example.test>' }],
    ['no email', { email: '' }],
    ['the patient role, which joins through the portal', { role: 'Patient' }],
    ['no facility', { facilityId: '' }],
  ])('refuses %s', (_, change) => expect(inviteSchema.safeParse({ ...valid, ...change }).success).toBe(false));

  it('shows ✕ only for no access', () => {
    expect(isAllowed('None')).toBe(false);
    expect(isAllowed('Read')).toBe(true);
  });
});
