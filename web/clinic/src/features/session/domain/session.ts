// Names mirror the enums in src/Modules/Tenancy/Medvya.Tenancy.Domain/RoleMatrix.cs (the server is the authority).
export type Role = 'SoloDoctor' | 'HospitalDoctor' | 'Receptionist' | 'Nurse' | 'CareCoordinator' | 'Patient' | 'PracticeAdmin' | 'HospitalIt';
export type Capability =
  | 'RegisterBookPatients' | 'CheckInQueue' | 'InvoicesPayments' | 'Patient360Chart' | 'VitalsAllergiesEntry' | 'ClinicalNotes'
  | 'PrescriptionsOrders' | 'RecordingConsentCapture' | 'AiDraftsEvidence' | 'ResultReviewAcknowledge' | 'CareTasksPatientContact'
  | 'ReleaseRecordsToPortal' | 'DevicePairingFleet' | 'StaffRolesIntegrations' | 'AuditLog';
export type Access = 'None' | 'Read' | 'Own' | 'Limited' | 'Released' | 'Full' | 'Sign' | 'Config' | 'BreakGlass';

export type Membership = { id: string; tenantId: string; tenantName: string; facilityId: string; facilityName: string; role: Role };
export type ActiveMembership = {
  membershipId: string; tenantId: string; facilityId: string; role: Role;
  capabilities: Partial<Record<Capability, Access>>;
  flags: string[];
};

/** /v1/me/context (contract agreed with MDV-P0-012). Older servers send only `user`: that means "no active facility". */
export type MeContext = { user: { id: string; displayName: string }; memberships?: Membership[]; active?: ActiveMembership | null };

/** The signed-in user's working context; fields the server left out are filled so nothing downstream guesses. */
export type Session = {
  user: { id: string; displayName: string };
  memberships: Membership[];
  active: (ActiveMembership & { facilityName: string }) | null;
};

export function toSession(me: MeContext): Session {
  const memberships = me.memberships ?? [];
  const a = me.active;
  if (!a) return { user: me.user, memberships, active: null };
  return {
    user: me.user, memberships,
    active: {
      ...a, capabilities: a.capabilities ?? {}, flags: a.flags ?? [],
      facilityName: memberships.find(m => m.id === a.membershipId)?.facilityName ?? '',
    },
  };
}

/**
 * Access levels that let a staff member see a screen in the clinic app.
 * Released (patient-only) and BreakGlass (an audited emergency path, never everyday navigation) are deliberately excluded.
 */
export const seeAccess: readonly Access[] = ['Read', 'Own', 'Limited', 'Full', 'Sign', 'Config'];
