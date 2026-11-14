import { describe, expect, it } from 'vitest';
import { toSession, type Access, type Capability, type Role } from '../../session/domain/session';
import { allows, homeItem, mobileTabs, navItems, patientDirectory, refusedAddress, visibleNav } from './navigation';

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
  it('lands a nurse on the nurse queue when intake is on, and everyone else on their first screen (UX-08)', () => {
    const home = (role: Role, flags: string[] = []) => {
      const active = { role, capabilities: capabilities(role), flags };
      return homeItem(visibleNav(active), active)?.id;
    };
    expect(home('Nurse', ['clinical.nurse-intake'])).toBe('nurse');
    expect(home('Nurse')).toBe('today');
    expect(home('SoloDoctor', ['clinical.nurse-intake'])).toBe('today');
    expect(home('HospitalDoctor', ['clinical.nurse-intake'])).toBe('today');
    expect(home('Receptionist', ['clinical.nurse-intake'])).toBe('frontDesk');
  });

  it('shows a receptionist only Practice items, never Clinical, even with every flag on', () => {
    expect(ids('Receptionist')).toEqual(['frontDesk', 'schedule', 'register']);
    expect(groups('Receptionist', allFlags)).toEqual(new Set(['practice']));
  });

  it('shows a doctor Clinical and Practice items allowed by the matrix', () => {
    expect(ids('SoloDoctor')).toEqual(['today', 'patients', 'frontDesk', 'schedule', 'register', 'settings']);
    expect(ids('SoloDoctor', allFlags)).toEqual(navItems.map(i => i.id));
    expect(ids('HospitalDoctor', allFlags)).not.toContain('billing'); // hospital doctors have no invoices access
  });

  it('hides items whose tenant flag is off', () => {
    expect(ids('SoloDoctor', ['care.messages'])).toContain('messages');
    expect(ids('SoloDoctor', [])).not.toContain('messages');
    for (const i of navItems) if (i.phase > 1) expect(i.flag, i.id).toBeTruthy();
  });

  it('shows Today (the day board until MDV-P2-002) to clinicians, who all see the board, and never to reception (#912)', () => {
    const clinicians = roles.filter(r => ids(r).includes('today'));
    expect(clinicians).toEqual(['SoloDoctor', 'HospitalDoctor', 'Nurse', 'CareCoordinator']);
    for (const r of clinicians) expect(ids(r)).toContain('frontDesk');
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

  it('says "your role does not include this screen" only for a screen the app has but the role may not open (MDV-P1-096)', () => {
    const refused = (role: Role, path: string, flags: string[] = []) => refusedAddress(path, { role, capabilities: capabilities(role), flags });
    // Receptionist: Today, Settings, a consultation and a chart are refused.
    for (const path of ['/today', '/settings', '/consult/e-1', '/consult/e-1/medicines', '/patients/p-1', '/patients'])
      expect(refused('Receptionist', path), path).toBe(true);
    // Nurse: Settings and a consultation (its note needs the full chart); practice admin: the chart.
    expect(refused('Nurse', '/settings')).toBe(true);
    expect(refused('Nurse', '/consult/e-1')).toBe(true);
    expect(refused('PracticeAdmin', '/patients/p-1')).toBe(true);
    // Not refused: unknown addresses, a switched-off screen, and a mistyped address under a screen the role has.
    expect(refused('Receptionist', '/no-such-page')).toBe(false);
    expect(refused('Receptionist', '/todayx')).toBe(false);
    expect(refused('SoloDoctor', '/messages')).toBe(false); // flag off: "not found"
    expect(refused('Nurse', '/messages', allFlags)).toBe(false); // flag on and allowed: a later route, not a refusal
    expect(refused('SoloDoctor', '/consult/e-1/nothing')).toBe(false);
    expect(refused('Receptionist', '/patients/directory/p-1/nothing')).toBe(false);
    expect(refused('Receptionist', '/admin/staff')).toBe(false); // removed (MDV-P1-107)
  });
});
