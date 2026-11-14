import { seeAccess, type Access, type ActiveMembership, type Capability } from '../../session/domain/session';

export type NavGroup = 'clinical' | 'practice' | 'assistant' | 'admin';
export type NavId =
  | 'today' | 'patients' | 'messages' | 'video' | 'referrals' | 'inbox' | 'carePlans'
  | 'frontDesk' | 'schedule' | 'nurse' | 'billing' | 'devices' | 'settings';

export type NavItem = {
  /** The group only colours the bar beside the current item; the menu is one list in the role's order (blueprint section 3). */
  id: NavId; path: string; group: NavGroup;
  /** Required capability and the access levels that count (default: any level that can see). */
  capability: Capability; access?: readonly Access[];
  /** Tenant feature flag (MDV-P0-028) that must be on; screens after the R1 launch (P1) all have one, so they stay hidden. */
  flag?: string;
  /** Delivery phase of the real screen; until then the route shows a placeholder. */
  phase: number;
};

// Clinician App sidebar (docs/02-design/source/Medvya Clinician App) and the role sitemap (docs/02-design/sitemap-and-permissions.md).
export const navItems: readonly NavItem[] = [
  // My day (address /today) is the doctor's own screen (MDV-P2-002), never the board: only Patient 360 at Full (MDV-P2-051).
  { id: 'today', path: '/today', group: 'clinical', capability: 'Patient360Chart', access: ['Full'], phase: 1 },
  // Patients opens the directory, so it follows the directory's API rule: Register / book patients at Read or Full.
  { id: 'patients', path: '/patients', group: 'clinical', capability: 'RegisterBookPatients', access: ['Read', 'Full'], phase: 1 },
  { id: 'messages', path: '/messages', group: 'clinical', capability: 'CareTasksPatientContact', flag: 'care.messages', phase: 4 },
  { id: 'video', path: '/video', group: 'clinical', capability: 'ClinicalNotes', flag: 'clinical.video', phase: 8 },
  { id: 'referrals', path: '/referrals', group: 'clinical', capability: 'ClinicalNotes', flag: 'clinical.referrals', phase: 2 },
  { id: 'inbox', path: '/inbox', group: 'clinical', capability: 'ResultReviewAcknowledge', flag: 'care.results-inbox', phase: 4 },
  { id: 'carePlans', path: '/care', group: 'clinical', capability: 'CareTasksPatientContact', flag: 'care.plans', phase: 4 },
  { id: 'frontDesk', path: '/front-desk', group: 'practice', capability: 'CheckInQueue', phase: 1 },
  { id: 'schedule', path: '/schedule', group: 'practice', capability: 'RegisterBookPatients', phase: 1 },
  { id: 'nurse', path: '/nurse', group: 'practice', capability: 'VitalsAllergiesEntry', flag: 'clinical.nurse-intake', phase: 2 },
  { id: 'billing', path: '/billing', group: 'practice', capability: 'InvoicesPayments', flag: 'billing.invoices', phase: 4 },
  { id: 'devices', path: '/devices', group: 'assistant', capability: 'DevicePairingFleet', flag: 'assistant.devices', phase: 3 },
  { id: 'settings', path: '/settings', group: 'admin', capability: 'StaffRolesIntegrations', phase: 1 },
];

type Guard = Pick<NavItem, 'capability' | 'access' | 'flag'>;
type Active = Pick<ActiveMembership, 'role' | 'capabilities' | 'flags'> | null;

/** True when this membership may use a screen: capability at an allowed level and, when gated, the tenant flag on. */
export function allows(active: Active, guard: Guard): boolean {
  // Patients use the portal (/p); their "Own" access is to their own records, never clinic screens.
  if (!active || active.role === 'Patient') return false;
  const level = active.capabilities[guard.capability] ?? 'None';
  return (guard.access ?? seeAccess).includes(level) && (!guard.flag || active.flags.includes(guard.flag));
}

/** Menu order per role (blueprint section 3); every other item follows in the order of navItems. */
const leads: Record<'doctor' | 'nurse' | 'desk', readonly NavId[]> = {
  doctor: ['today', 'patients', 'schedule', 'frontDesk'],
  nurse: ['nurse', 'patients'],
  desk: ['frontDesk', 'schedule', 'patients'],
};

/**
 * Items this membership may see, in its role's order; the first is where the start address lands. A doctor (My day) starts on My day;
 * a nurse (vitals at Full, no full chart, no booking) on the nurse queue when intake is on (UX-08); everyone else on Front desk.
 * Hiding is UX only; the API enforces.
 */
export function visibleNav(active: Active): NavItem[] {
  const items = navItems.filter(i => allows(active, i));
  const c = active?.capabilities;
  const nurse = c?.VitalsAllergiesEntry === 'Full' && c.Patient360Chart !== 'Full' && c.RegisterBookPatients !== 'Full';
  const has = (id: NavId) => items.some(i => i.id === id);
  const lead = leads[has('today') ? 'doctor' : nurse && has('nurse') ? 'nurse' : 'desk'];
  const rank = (i: NavItem) => (lead.includes(i.id) ? lead.indexOf(i.id) : lead.length);
  return items.sort((a, b) => rank(a) - rank(b)); // stable: the rest keep navItems order, Settings last
}

/** Register patient (P-02): a button on Front desk and Patients and a search action, not a menu item (MDV-P2-051). */
export const registerPatient = { path: '/patients/new', capability: 'RegisterBookPatients', access: ['Full'] } as const satisfies Guard & { path: string };

/** The clinical record (Summary, Timeline): any level of Patient 360 a clinician works at; break-glass is not everyday access. */
export const clinicalRecord = { capability: 'Patient360Chart', access: ['Read', 'Limited', 'Full', 'Sign'] } as const satisfies Guard;

/**
 * Patient directory and search (MDV-P1-018), opened from the header search, not the sidebar, so every role that may look patients
 * up reaches it the same way. Same rule as the API policy: Register / book patients at Read or Full.
 */
export const patientDirectory = { path: '/patients/directory', capability: 'RegisterBookPatients', access: ['Read', 'Full'] } as const satisfies Guard & { path: string };

/**
 * For an address no route of this membership matched: true when the app has that screen but this role may not open it, so the page
 * says so instead of "Page not found" (MDV-P1-096). A switched-off or unknown address stays "not found". UX only; the API enforces.
 */
export function refusedAddress(path: string, active: Active): boolean {
  const under = (p: string) => path === p || path.startsWith(`${p}/`);
  // A consultation needs the full chart, like its note API.
  if (under('/consult')) return active?.capabilities.Patient360Chart !== 'Full';
  if (under(patientDirectory.path)) return !allows(active, patientDirectory);
  // A patient's Summary and Timeline (/patients/:id) need the clinical record; Patients itself and Register patient are routes of their own.
  if (path.startsWith('/patients/') && !under(registerPatient.path)) return !allows(active, clinicalRecord);
  return navItems.some(i => under(i.path) && (!i.flag || !!active?.flags.includes(i.flag)) && !allows(active, i));
}

/** Mobile bottom bar: the first four items of the role's menu, then "More". */
export const mobileTabs = (visible: NavItem[]): NavItem[] => visible.slice(0, 4);
