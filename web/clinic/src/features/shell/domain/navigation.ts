import { seeAccess, type Access, type ActiveMembership, type Capability } from '../../session/domain/session';

export type NavGroup = 'clinical' | 'practice' | 'assistant' | 'admin';
export type NavId =
  | 'today' | 'patients' | 'messages' | 'video' | 'referrals' | 'inbox' | 'carePlans'
  | 'frontDesk' | 'schedule' | 'register' | 'nurse' | 'billing' | 'devices' | 'settings';

export type NavItem = {
  id: NavId; path: string; group: NavGroup;
  /** Required capability and the access levels that count (default: any level that can see). */
  capability: Capability; access?: readonly Access[];
  /** Tenant feature flag (MDV-P0-028) that must be on; screens after the R1 launch (P1) all have one, so they stay hidden. */
  flag?: string;
  /** Delivery phase of the real screen; until then the route shows a placeholder. */
  phase: number;
};

// Clinician App sidebar (design/Medvya Clinician App) and the role sitemap (docs/02-design/sitemap-and-permissions.md).
export const navItems: readonly NavItem[] = [
  { id: 'today', path: '/today', group: 'clinical', capability: 'Patient360Chart', flag: 'clinical.today', phase: 2 },
  { id: 'patients', path: '/patients', group: 'clinical', capability: 'Patient360Chart', phase: 1 },
  { id: 'messages', path: '/messages', group: 'clinical', capability: 'CareTasksPatientContact', flag: 'care.messages', phase: 4 },
  { id: 'video', path: '/video', group: 'clinical', capability: 'ClinicalNotes', flag: 'clinical.video', phase: 8 },
  { id: 'referrals', path: '/referrals', group: 'clinical', capability: 'ClinicalNotes', flag: 'clinical.referrals', phase: 2 },
  { id: 'inbox', path: '/inbox', group: 'clinical', capability: 'ResultReviewAcknowledge', flag: 'care.results-inbox', phase: 4 },
  { id: 'carePlans', path: '/care', group: 'clinical', capability: 'CareTasksPatientContact', flag: 'care.plans', phase: 4 },
  { id: 'frontDesk', path: '/front-desk', group: 'practice', capability: 'CheckInQueue', phase: 1 },
  { id: 'schedule', path: '/schedule', group: 'practice', capability: 'RegisterBookPatients', phase: 1 },
  { id: 'register', path: '/patients/new', group: 'practice', capability: 'RegisterBookPatients', access: ['Full'], phase: 1 },
  { id: 'nurse', path: '/nurse', group: 'practice', capability: 'VitalsAllergiesEntry', flag: 'clinical.nurse-intake', phase: 2 },
  { id: 'billing', path: '/billing', group: 'practice', capability: 'InvoicesPayments', flag: 'billing.invoices', phase: 4 },
  { id: 'devices', path: '/devices', group: 'assistant', capability: 'DevicePairingFleet', flag: 'assistant.devices', phase: 3 },
  { id: 'settings', path: '/settings', group: 'admin', capability: 'StaffRolesIntegrations', phase: 1 },
];

export const navGroups: readonly NavGroup[] = ['clinical', 'practice', 'assistant', 'admin'];

type Guard = Pick<NavItem, 'capability' | 'access' | 'flag'>;
type Active = Pick<ActiveMembership, 'role' | 'capabilities' | 'flags'> | null;

/** True when this membership may use a screen: capability at an allowed level and, when gated, the tenant flag on. */
export function allows(active: Active, guard: Guard): boolean {
  // Patients use the portal (/p); their "Own" access is to their own records, never clinic screens.
  if (!active || active.role === 'Patient') return false;
  const level = active.capabilities[guard.capability] ?? 'None';
  return (guard.access ?? seeAccess).includes(level) && (!guard.flag || active.flags.includes(guard.flag));
}

/** Items this membership may see. Hiding is UX only; the API enforces. */
export const visibleNav = (active: Active): NavItem[] => navItems.filter(i => allows(active, i));

/**
 * Patient directory and search (MDV-P1-018), opened from the header search, not the sidebar, so every role that may look patients
 * up reaches it the same way. Same rule as the API policy: Register / book patients at Read or Full.
 */
export const patientDirectory = { path: '/patients/directory', capability: 'RegisterBookPatients', access: ['Read', 'Full'] } as const satisfies Guard & { path: string };

const tabPriority: readonly NavId[] = ['today', 'patients', 'inbox', 'messages', 'frontDesk', 'schedule', 'register', 'nurse'];

/** Mobile bottom bar: up to four of the visible items (design order Today · Patients · Inbox · Messages), then "More". */
export const mobileTabs = (visible: NavItem[]): NavItem[] =>
  tabPriority.flatMap(id => visible.filter(i => i.id === id)).slice(0, 4);
