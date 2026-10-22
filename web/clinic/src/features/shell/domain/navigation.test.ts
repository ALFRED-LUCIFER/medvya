import { describe, expect, it } from 'vitest';
import { toSession, type Access, type Capability, type Role } from '../../session/domain/session';
import { allows, mobileTabs, navItems, patientDirectory, visibleNav } from './navigation';

// Copy of RoleMatrix.cs rows (what /v1/me/context sends as `capabilities` for each role).
const roles: Role[] = ['SoloDoctor', 'HospitalDoctor', 'Receptionist', 'Nurse', 'CareCoordinator', 'Patient', 'PracticeAdmin', 'HospitalIt'];
const codes: Record<string, Access> = { F: 'Full', R: 'Read', O: 'Own', L: 'Limited', P: 'Released', S: 'Sign', C: 'Config', B: 'BreakGlass', X: 'None' };
const rows: [Capability, string][] = [
  ['RegisterBookPatients', 'F F F R R O F X'], ['CheckInQueue', 'F R F F R X R X'], ['InvoicesPayments', 'F X F X X O F X'],
  ['Patient360Chart', 'F F X L L P X B'], ['VitalsAllergiesEntry', 'F F X F X X X X'], ['ClinicalNotes', 'S S X R R P X X'],
  ['PrescriptionsOrders', 'S S X X X P X X'], ['RecordingConsentCapture', 'F F X L X O X X'], ['AiDraftsEvidence', 'F F X X X X X X'],
  ['ResultReviewAcknowledge', 'S S X X R X X X'], ['CareTasksPatientContact', 'F F X F F X X X'], ['ReleaseRecordsToPortal', 'F F X X L O X X'],
  ['DevicePairingFleet', 'O O X O X X C C'], ['StaffRolesIntegrations', 'C X X X X X C C'], ['AuditLog', 'R X X X X X R F'],
];
const capabilities = (role: Role) =>
  Object.fromEntries(rows.map(([cap, c]) => [cap, codes[c.split(' ')[roles.indexOf(role)]!]!])) as Record<Capability, Access>;
const allFlags = navItems.flatMap(i => (i.flag ? [i.flag] : []));
const ids = (role: Role, flags: string[] = []) => visibleNav({ role, capabilities: capabilities(role), flags }).map(i => i.id);
const groups = (role: Role, flags: string[] = []) => new Set(visibleNav({ role, capabilities: capabilities(role), flags }).map(i => i.group));

describe('role-based navigation', () => {
  it('shows a receptionist only Practice items, never Clinical, even with every flag on', () => {
    expect(ids('Receptionist')).toEqual(['frontDesk', 'schedule', 'register']);
    expect(groups('Receptionist', allFlags)).toEqual(new Set(['practice']));
  });

  it('shows a doctor Clinical and Practice items allowed by the matrix', () => {
    expect(ids('SoloDoctor')).toEqual(['patients', 'frontDesk', 'schedule', 'register', 'settings']);
    expect(ids('SoloDoctor', allFlags)).toEqual(navItems.map(i => i.id));
    expect(ids('HospitalDoctor', allFlags)).not.toContain('billing'); // hospital doctors have no invoices access
  });

  it('hides items whose tenant flag is off', () => {
    expect(ids('SoloDoctor', ['clinical.today'])).toContain('today');
    expect(ids('SoloDoctor', [])).not.toContain('today');
    for (const i of navItems) if (i.phase > 1) expect(i.flag, i.id).toBeTruthy();
  });

  it('needs write access to register, and never treats break-glass or patient access as everyday navigation', () => {
    expect(ids('Nurse')).not.toContain('register'); // Read only
    expect(ids('HospitalIt', allFlags)).not.toContain('patients'); // BreakGlass
    expect(ids('Patient', allFlags)).toEqual([]); // Own / Released belong to the portal
  });

  it('shows nothing without an active facility, including for an older server that sends only the user', () => {
    const s = toSession({ user: { id: 'u1', displayName: 'Dr. Arjun Rao' } });
    expect(s.active).toBeNull();
    expect(visibleNav(s.active)).toEqual([]);
  });

  it('fills the facility name and tolerates missing capability and flag fields', () => {
    const s = toSession({
      user: { id: 'u1', displayName: 'A. Joseph' },
      memberships: [{ id: 'm1', tenantId: 't1', tenantName: 'Iyer Family Clinic', facilityId: 'f1', facilityName: 'Adyar', role: 'Receptionist' }],
      active: { membershipId: 'm1', tenantId: 't1', facilityId: 'f1', role: 'Receptionist' } as never,
    });
    expect(s.active?.facilityName).toBe('Adyar');
    expect(visibleNav(s.active)).toEqual([]);
  });

  it('opens the patient directory to the roles the API lets search (Register / book patients: Read or Full)', () => {
    const may = (role: Role) => allows({ role, capabilities: capabilities(role), flags: [] }, patientDirectory);
    expect(roles.filter(may)).toEqual(['SoloDoctor', 'HospitalDoctor', 'Receptionist', 'Nurse', 'CareCoordinator', 'PracticeAdmin']);
    expect(allows(null, patientDirectory)).toBe(false);
  });

  it('puts at most four tabs in the mobile bar, in the design order', () => {
    expect(mobileTabs(visibleNav({ role: 'SoloDoctor', capabilities: capabilities('SoloDoctor'), flags: allFlags })).map(i => i.id))
      .toEqual(['today', 'patients', 'inbox', 'messages']);
    expect(mobileTabs(visibleNav({ role: 'Receptionist', capabilities: capabilities('Receptionist'), flags: [] })).map(i => i.id))
      .toEqual(['frontDesk', 'schedule', 'register']);
  });
});
