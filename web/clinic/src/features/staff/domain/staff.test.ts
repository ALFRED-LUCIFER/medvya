import { describe, expect, it } from 'vitest';
import { canChangeRole, isOwnerAmong, offboardBlock, roleOfValue, addableRows, canOffboard, canResend, canReactivate, canSetLocations, locationsOf, permissionRows, reasonMissing, rebookingMissing, removesLocation, statusLabel, type RoleCatalogue, type StaffMember } from './staff';

const catalogue: RoleCatalogue = {
  builtIn: [
    { role: 'Receptionist', rows: [{ capability: 'ClinicalNotes', access: 'None', extra: null }, { capability: 'AuditLog', access: 'None', extra: 'Read' }, { capability: 'CheckInQueue', access: 'Full', extra: null }] },
    { role: 'Nurse', rows: [] }, { role: 'CareCoordinator', rows: [] }, { role: 'PracticeAdmin', rows: [] },
  ],
  custom: [{ id: 'c1', name: 'Desk with audit', baseRole: 'Receptionist', capabilities: ['AuditLog'] }],
};

describe('permissionRows', () => {
  it('shows a built-in role as the matrix column', () => {
    expect(permissionRows(catalogue, { kind: 'builtIn', role: 'Receptionist' }).map(r => [r.capability, r.access, r.added])).toEqual([
      ['ClinicalNotes', 'None', false], ['AuditLog', 'None', false], ['CheckInQueue', 'Full', false],
    ]);
  });

  it('adds a custom role extras at the level the server offers, never clinical rows', () => {
    const rows = permissionRows(catalogue, { kind: 'custom', id: 'c1' });
    expect(rows.find(r => r.capability === 'AuditLog')).toEqual({ capability: 'AuditLog', access: 'Read', added: true });
    expect(rows.find(r => r.capability === 'ClinicalNotes')?.access).toBe('None');
  });

  it('has no rows for Doctor: nothing is created here', () => {
    expect(permissionRows(catalogue, { kind: 'doctor' })).toEqual([]);
  });
});

describe('canOffboard', () => {
  const base = { membershipId: 'm', userId: null, name: null, email: null, role: 'Nurse', customRoleId: null, customRoleName: null, facilityId: 'f', facilityName: 'F', mfa: 'enrolled', invitationId: null, invitationState: null, invitationExpiresAt: null, isOwner: false } as const;
  it('is offered for people who joined, not invitees or the already offboarded', () => {
    expect((['Active', 'Suspended', 'Invited', 'Offboarded'] as const).map(status => canOffboard({ ...base, status }))).toEqual([true, true, false, false]);
  });

  it('labels a revoked or expired invitation as such, not as Invited (MDV-P1-052)', () => {
    expect((['pending', 'revoked', 'expired'] as const).map(invitationState => statusLabel({ ...base, status: 'Invited', invitationState }))).toEqual(['Invited', 'Revoked', 'Expired']);
    expect(statusLabel({ ...base, status: 'Active' })).toBe('Active');
  });

  it('offers Resend on a pending or expired invitation, never a revoked one (#912)', () => {
    expect((['pending', 'expired', 'revoked'] as const).map(invitationState => canResend({ ...base, status: 'Invited', invitationId: 'i', invitationState }))).toEqual([true, true, false]);
  });
});

describe('addableRows', () => {
  it('lists only rows the server marked addable', () => {
    expect(addableRows(catalogue, 'Receptionist').map(r => r.capability)).toEqual(['AuditLog']);
  });
});

describe('offboarding and re-activation (#225)', () => {
  const m = (over: Partial<StaffMember>): StaffMember => ({
    membershipId: 'm', userId: 'u', name: null, email: null, role: 'Receptionist', customRoleId: null, customRoleName: null, facilityId: 'f', facilityName: 'Adyar',
    status: 'Active', mfa: 'enrolled', invitationId: null, invitationState: null, invitationExpiresAt: null, isOwner: false, ...over,
  });
  it('asks a doctor\'s offboarding what happens to their patients, and nobody else\'s', () => {
    expect(rebookingMissing(m({ role: 'HospitalDoctor' }), { reason: 'x', rebooking: null })).toBe(true);
    expect(rebookingMissing(m({ role: 'SoloDoctor' }), { reason: 'x', rebooking: 'call' })).toBe(false);
    expect(rebookingMissing(m({ role: 'Receptionist' }), { reason: 'x', rebooking: null })).toBe(false);
  });
  // MDV-P1-107: the server refuses the same; these only decide what the row offers.
  it('offers Offboard on an owner or a doctor only to an owner, and never on your own row', () => {
    const owner = m({ userId: 'o', role: 'SoloDoctor', isOwner: true }), doctor = m({ userId: 'd', role: 'HospitalDoctor' }), desk = m({ userId: 'r' });
    expect([owner, doctor, desk].map(x => offboardBlock(x, 'admin', false))).toEqual(['owner', 'doctor', null]);
    expect([owner, doctor, desk].map(x => offboardBlock(x, 'o', true))).toEqual(['self', null, null]);
    expect(isOwnerAmong([owner, desk], 'o')).toBe(true);
    expect(isOwnerAmong([owner, desk], 'r')).toBe(false);
  });
  it('changes the role in place of staff the table assigns, never an owner, a doctor or oneself', () => {
    expect([m({ userId: 'r' }), m({ userId: 'o', isOwner: true, role: 'PracticeAdmin' }), m({ userId: 'd', role: 'HospitalDoctor' }), m({ userId: 'me' }), m({ userId: 'x', status: 'Offboarded' })]
      .map(x => canChangeRole(x, 'me'))).toEqual([true, false, false, false, false]);
    const catalogue = { builtIn: [], custom: [{ id: 'c1', name: 'Front desk lead', baseRole: 'Receptionist' as const, capabilities: [] }] };
    expect([roleOfValue(catalogue, 'builtIn:Nurse'), roleOfValue(catalogue, 'custom:c1'), roleOfValue(catalogue, 'builtIn:SoloDoctor'), roleOfValue(catalogue, 'custom:zz')])
      .toEqual([{ role: 'Nurse', customRoleId: null }, { role: 'Receptionist', customRoleId: 'c1' }, null, null]);
  });
  it('brings back only someone offboarded who had joined', () => {
    expect(canReactivate(m({ status: 'Offboarded' }))).toBe(true);
    expect(canReactivate(m({ status: 'Offboarded', userId: null }))).toBe(false);
    expect(canReactivate(m({ status: 'Active' }))).toBe(false);
  });
});

describe('locations (MDV-P1-071)', () => {
  const row = (membershipId: string, userId: string | null, facilityId: string, status: StaffMember['status'] = 'Active'): StaffMember => ({
    membershipId, userId, name: null, email: null, role: 'Receptionist', customRoleId: null, customRoleName: null, facilityId, facilityName: facilityId,
    status, mfa: 'enrolled', invitationId: null, invitationState: null, invitationExpiresAt: null, isOwner: false,
  });
  const rows = [row('1', 'u', 'adyar'), row('2', 'u', 'velachery'), row('3', 'u', 'mylapore', 'Offboarded'), row('4', 'v', 'adyar')];
  it('lists the active locations of the same person only', () => {
    expect(locationsOf(rows, rows[0]!)).toEqual(['adyar', 'velachery']);
    expect(locationsOf(rows, rows[3]!)).toEqual(['adyar']);
  });
  it('edits only people who have joined and are active', () => {
    expect([row('a', 'u', 'f'), row('b', null, 'f', 'Invited'), row('c', 'u', 'f', 'Offboarded')].map(canSetLocations)).toEqual([true, false, false]);
  });
  it('needs a reason only when a location is taken away', () => {
    expect(removesLocation(['a', 'b'], ['a', 'b', 'c'])).toBe(false);
    expect(removesLocation(['a', 'b'], ['a'])).toBe(true);
    expect(reasonMissing(['a', 'b'], { facilityIds: ['a'], reason: '' })).toBe(true);
    expect(reasonMissing(['a', 'b'], { facilityIds: ['a', 'b', 'c'], reason: '' })).toBe(false);
  });
});
