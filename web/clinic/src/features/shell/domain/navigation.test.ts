import { describe, expect, it } from 'vitest';
import { toSession, type Access, type Capability, type Role } from '../../session/domain/session';
import { allows, clinicalRecord, mobileTabs, navItems, patientDirectory, refusedAddress, registerPatient, visibleNav } from './navigation';

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

describe('role-based navigation', () => {
  it('lands a nurse on the nurse queue when intake is on, a doctor on My day, and everyone else on Front desk (UX-08)', () => {
    const home = (role: Role, flags: string[] = []) => visibleNav({ role, capabilities: capabilities(role), flags })[0]?.id;
    expect(home('Nurse', ['clinical.nurse-intake'])).toBe('nurse');
    expect(home('Nurse')).toBe('frontDesk');
    expect(home('CareCoordinator')).toBe('frontDesk');
    expect(home('SoloDoctor', ['clinical.nurse-intake'])).toBe('today');
    expect(home('HospitalDoctor', ['clinical.nurse-intake'])).toBe('today');
    expect(home('Receptionist', ['clinical.nurse-intake'])).toBe('frontDesk');
  });

  // Blueprint section 3 (MDV-P2-051): each role's menu, in its order. Register patient is a button, never a menu item.
  it('orders each role\'s menu as the blueprint\'s role table', () => {
    expect(ids('SoloDoctor')).toEqual(['today', 'patients', 'schedule', 'frontDesk', 'settings']);
    expect(ids('HospitalDoctor')).toEqual(['today', 'patients', 'schedule', 'frontDesk']); // owner-only Settings
    expect(ids('Receptionist')).toEqual(['frontDesk', 'schedule', 'patients']);
    expect(ids('Nurse', ['clinical.nurse-intake'])).toEqual(['nurse', 'patients', 'frontDesk', 'schedule']);
    expect(ids('PracticeAdmin')).toEqual(['frontDesk', 'schedule', 'patients', 'settings']);
    // Every other item follows the named ones, Settings last.
    expect(ids('SoloDoctor', allFlags).slice(0, 4)).toEqual(['today', 'patients', 'schedule', 'frontDesk']);
    expect(ids('SoloDoctor', allFlags).at(-1)).toBe('settings');
    expect(new Set(ids('SoloDoctor', allFlags))).toEqual(new Set(navItems.map(i => i.id)));
    expect(ids('HospitalDoctor', allFlags)).not.toContain('billing'); // hospital doctors have no invoices access
    for (const r of roles) expect(ids(r, allFlags).map(id => navItems.find(i => i.id === id)!.path)).not.toContain(registerPatient.path);
  });

  it('never shows a receptionist a clinical screen: Patients is the directory, not the chart', () => {
    expect(ids('Receptionist', allFlags)).toEqual(['frontDesk', 'schedule', 'patients', 'billing']);
    expect(allows({ role: 'Receptionist', capabilities: capabilities('Receptionist'), flags: [] }, clinicalRecord)).toBe(false);
  });

  it('hides items whose tenant flag is off', () => {
    expect(ids('SoloDoctor', ['care.messages'])).toContain('messages');
    expect(ids('SoloDoctor', [])).not.toContain('messages');
    for (const i of navItems) if (i.phase > 1) expect(i.flag, i.id).toBeTruthy();
  });

  it('shows My day only to doctors, never the board: a role that only gets the board no longer sees it (MDV-P2-051)', () => {
    expect(roles.filter(r => ids(r, allFlags).includes('today'))).toEqual(['SoloDoctor', 'HospitalDoctor']);
    // AC2: a nurse with intake off gets Front desk once, not also as "Today" (which used to open the same board).
    expect(ids('Nurse')).toEqual(['frontDesk', 'schedule', 'patients']);
  });

  it('needs write access to register, and never treats break-glass or patient access as everyday navigation', () => {
    const may = (role: Role) => allows({ role, capabilities: capabilities(role), flags: [] }, registerPatient);
    expect(roles.filter(may)).toEqual(['SoloDoctor', 'HospitalDoctor', 'Receptionist', 'PracticeAdmin']); // a nurse reads only
    expect(ids('HospitalIt', allFlags)).not.toContain('patients'); // no directory access
    expect(allows({ role: 'HospitalIt', capabilities: capabilities('HospitalIt'), flags: [] }, clinicalRecord)).toBe(false); // BreakGlass
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

  it('opens the patient directory, and the Patients menu item, to the roles the API lets search (Register / book patients: Read or Full)', () => {
    const may = (role: Role) => allows({ role, capabilities: capabilities(role), flags: [] }, patientDirectory);
    expect(roles.filter(may)).toEqual(['SoloDoctor', 'HospitalDoctor', 'Receptionist', 'Nurse', 'CareCoordinator', 'PracticeAdmin']);
    expect(roles.filter(r => ids(r).includes('patients'))).toEqual(roles.filter(may));
    expect(allows(null, patientDirectory)).toBe(false);
  });

  it('puts the first four items of the role\'s menu in the mobile bar', () => {
    expect(mobileTabs(visibleNav({ role: 'SoloDoctor', capabilities: capabilities('SoloDoctor'), flags: allFlags })).map(i => i.id))
      .toEqual(['today', 'patients', 'schedule', 'frontDesk']);
    expect(mobileTabs(visibleNav({ role: 'Receptionist', capabilities: capabilities('Receptionist'), flags: [] })).map(i => i.id))
      .toEqual(['frontDesk', 'schedule', 'patients']);
  });

  it('says "your role does not include this screen" only for a screen the app has but the role may not open (MDV-P1-096)', () => {
    const refused = (role: Role, path: string, flags: string[] = []) => refusedAddress(path, { role, capabilities: capabilities(role), flags });
    // Receptionist: My day, Settings, a consultation and a patient's Summary or Timeline are refused (Patients is theirs: MDV-P2-051).
    for (const path of ['/today', '/settings', '/consult/e-1', '/consult/e-1/medicines', '/patients/p-1', '/patients/p-1/timeline'])
      expect(refused('Receptionist', path), path).toBe(true);
    // Nurse: Settings and a consultation (its note needs the full chart); practice admin: the chart.
    expect(refused('Nurse', '/settings')).toBe(true);
    expect(refused('Nurse', '/consult/e-1')).toBe(true);
    expect(refused('PracticeAdmin', '/patients/p-1')).toBe(true);
    expect(refused('SoloDoctor', '/patients/p-1/nothing')).toBe(false);
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
