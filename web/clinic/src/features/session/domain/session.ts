import type { Country } from '@medvya/i18n';

// Names mirror the enums in src/Modules/Tenancy/Medvya.Tenancy.Domain/RoleMatrix.cs (the server is the authority).
export type Role = 'SoloDoctor' | 'HospitalDoctor' | 'Receptionist' | 'Nurse' | 'CareCoordinator' | 'Patient' | 'PracticeAdmin' | 'HospitalIt';
export type Capability =
  | 'RegisterBookPatients' | 'CheckInQueue' | 'InvoicesPayments' | 'Patient360Chart' | 'VitalsAllergiesEntry' | 'ClinicalNotes'
  | 'PrescriptionsOrders' | 'RecordingConsentCapture' | 'AiDraftsEvidence' | 'ResultReviewAcknowledge' | 'CareTasksPatientContact'
  | 'ReleaseRecordsToPortal' | 'DevicePairingFleet' | 'StaffRolesIntegrations' | 'AuditLog';
export type Access = 'None' | 'Read' | 'Own' | 'Limited' | 'Released' | 'Full' | 'Sign' | 'Config' | 'BreakGlass';

/** Mirrors Medvya.Tenancy.Domain.OrganisationState (ADR-016). */
export type OrganisationState = 'Draft' | 'Submitted' | 'NeedsChanges' | 'Approved' | 'Rejected' | 'Suspended';

export type Membership = { id: string; tenantId: string; tenantName: string; facilityId: string; facilityName: string; role: Role };
export type ActiveMembership = {
  membershipId: string; tenantId: string; facilityId: string; role: Role;
  /** The tenant's country: picks registration rules and formats. Set by the server, never chosen in the client. */
  country?: Country;
  /** The facility's IANA time zone (set by the server): screens that show instants use it, not the browser's. */
  timeZone?: string;
  capabilities: Partial<Record<Capability, Access>>;
  flags: string[];
  /** Where the organisation stands with Medvya (ADR-016). Before Approved only set-up is open; Suspended is read-only. The server already narrowed `capabilities`. */
  organisationState?: OrganisationState;
};

/** /v1/me/context (contract agreed with MDV-P0-012). Older servers send only `user`: that means "no active facility". */
export type MeContext = {
  user: { id: string; displayName: string }; memberships?: Membership[]; active?: ActiveMembership | null;
  /** Set by the server when the user has no active membership but lost one (suspended, offboarded): F-05b, not the setup wizard. */
  accessEnded?: boolean;
  /** The membership the user last worked at (from their own sessions, MDV-P1-059); always one of `memberships`. */
  lastMembershipId?: string | null;
  /** When this browser session ends if nothing more is done (#1365 F-057); older servers leave it out, and then nothing is warned. */
  session?: SessionEnd | null;
};

/**
 * The server's own reckoning at the moment it answered: seconds until the session ends, whether using the app still moves that
 * (not once the 8-hour limit comes first), and the clinic's idle time-out.
 */
export type SessionEnd = { endsInSeconds: number; extendable: boolean; idleTimeoutSeconds: number };

/** How long before the end the app says so: two minutes, or half the time-out where a clinic's is shorter than four. */
const noticeSeconds = (end: SessionEnd) => Math.min(120, end.idleTimeoutSeconds / 2);
export const endingSoon = (end: SessionEnd | null | undefined): boolean => !!end && end.endsInSeconds <= noticeSeconds(end);

/**
 * When to ask the server again: each minute, sooner when the notice is due before that, and right after the end once it is showing,
 * so "signed out" appears when it happens. Never under five seconds apart.
 */
export function nextPollMs(end: SessionEnd | null | undefined): number {
  if (!end) return 60_000;
  const seconds = endingSoon(end) ? end.endsInSeconds + 1 : end.endsInSeconds - noticeSeconds(end);
  return Math.min(60_000, Math.max(5_000, seconds * 1000));
}

/** The signed-in user's working context; fields the server left out are filled so nothing downstream guesses. */
export type Session = {
  user: { id: string; displayName: string };
  /** Last used first, then as the server sent them. */
  memberships: Membership[];
  lastMembershipId: string | null;
  accessEnded: boolean;
  /** `timeZone` is always set: instants are never shown in the browser's zone (AGENTS.md 3.5). */
  active: (ActiveMembership & { facilityName: string; timeZone: string }) | null;
};

/** The picker lists the last used facility first (F-03); the rest keep the server's order. */
export function lastUsedFirst(memberships: Membership[], lastId: string | null | undefined): Membership[] {
  const last = memberships.find(m => m.id === lastId);
  return last ? [last, ...memberships.filter(m => m !== last)] : memberships;
}

export function toSession(me: MeContext): Session {
  const lastMembershipId = me.lastMembershipId ?? null;
  const memberships = lastUsedFirst(me.memberships ?? [], lastMembershipId);
  const a = me.active, accessEnded = me.accessEnded ?? false;
  if (!a) return { user: me.user, memberships, lastMembershipId, accessEnded, active: null };
  return {
    user: me.user, memberships, lastMembershipId, accessEnded,
    active: {
      ...a, capabilities: a.capabilities ?? {}, flags: a.flags ?? [],
      // The server always sends the facility's zone; an older or partial answer falls back to the country's, never the browser's.
      timeZone: a.timeZone ?? (a.country === 'AE' ? 'Asia/Dubai' : 'Asia/Kolkata'),
      facilityName: memberships.find(m => m.id === a.membershipId)?.facilityName ?? '',
    },
  };
}

/**
 * Access levels that let a staff member see a screen in the clinic app.
 * Released (patient-only) and BreakGlass (an audited emergency path, never everyday navigation) are deliberately excluded.
 */
export const seeAccess: readonly Access[] = ['Read', 'Own', 'Limited', 'Full', 'Sign', 'Config'];

/** What the client knows about its session: none, one that ended under it (F-05a), or a working context. */
export type SessionStatus = { kind: 'signedOut' } | { kind: 'expired'; idle?: true } | { kind: 'signedIn'; me: MeContext; end?: SessionEnd | null };

/**
 * No session now: "expired" if this tab had one (idle timeout, revoked), otherwise simply signed out. The end time is kept beside `me`,
 * not in it: it changes with every answer, and the working context must keep its identity while nothing about it changed.
 * `before` is what the tab knew last: an idle notice that was showing means the session ended for lack of use, and the page says so.
 * shortcut: after a reload the reason is gone and the page gives the general explanation; keep it per tab if desks ask why.
 */
export function nextSession(hadSession: boolean, me: MeContext | null, before?: SessionStatus): SessionStatus {
  if (me) {
    const { session: end, ...context } = me;
    return end ? { kind: 'signedIn', me: context, end } : { kind: 'signedIn', me: context };
  }
  if (!hadSession) return { kind: 'signedOut' };
  return before?.kind === 'signedIn' && endingSoon(before.end) && before.end?.extendable ? { kind: 'expired', idle: true } : { kind: 'expired' };
}
