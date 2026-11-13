import { describe, expect, it } from 'vitest';
import type { StaffMember } from './staff';
import { staffActions } from './staffTable';

const m = (over: Partial<StaffMember>): StaffMember => ({
  membershipId: 'm', userId: 'u', name: null, email: null, role: 'Receptionist', customRoleId: null, customRoleName: null, facilityId: 'f', facilityName: 'Adyar',
  status: 'Active', mfa: 'enrolled', invitationId: null, invitationState: null, invitationExpiresAt: null, isOwner: false, ...over,
});

// MDV-P2-054 AC6: Offboard and Revoke are never the compact visible button; they stay 44 px menu items.
describe('staffActions', () => {
  it('shows a form-opening action and keeps what acts at once or ends access in the menu', () => {
    expect(staffActions(m({}), 'me', true, true)).toEqual({ visible: 'changeRole', menu: ['locations', 'offboard'] });
    expect(staffActions(m({}), 'me', true, false)).toEqual({ visible: 'changeRole', menu: ['offboard'] });
    expect(staffActions(m({ status: 'Invited', userId: null, invitationId: 'i', invitationState: 'pending' }), 'me', true, true)).toEqual({ visible: null, menu: ['resend', 'revoke'] });
    expect(staffActions(m({ status: 'Offboarded' }), 'me', true, true)).toEqual({ visible: 'reactivate', menu: [] });
  });
  it('never makes Offboard the visible action, and leaves it out where this user may not offboard', () => {
    const doctor = m({ userId: 'd', role: 'HospitalDoctor' });
    expect(staffActions(doctor, 'me', true, false)).toEqual({ visible: null, menu: ['offboard'] });
    expect(staffActions(doctor, 'me', false, false)).toEqual({ visible: null, menu: [] });
    expect(staffActions(m({ userId: 'me' }), 'me', true, false)).toEqual({ visible: null, menu: [] });
  });
});
