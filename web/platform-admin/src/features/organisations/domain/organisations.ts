import type { PlatformRole } from '../../session/domain/session';

export type OrganisationState = 'Draft' | 'Submitted' | 'NeedsChanges' | 'Approved' | 'Rejected' | 'Suspended';
export type OrganisationType = 'Clinic' | 'Hospital';
export type OrganisationDocument = { id: string; kind: string; contentType: string; sizeBytes: number; uploadedAt: string };

/** GET /v1/organisations: control-plane facts and document metadata only, never clinic data (MDV-P1-064). */
export type QueueItem = {
  tenantId: string; name: string; country: string; type: OrganisationType; state: OrganisationState; submittedAt: string | null;
  reason: string | null; itContactName: string | null; itContactEmail: string | null; documents: OrganisationDocument[];
};

export type HistoryEntry = { at: string; actor: string | null; state: OrganisationState; reason: string | null };

export type OrganisationDetail = {
  name: string; country: string; type: OrganisationType; state: OrganisationState; submittedAt: string | null; reason: string | null;
  address: string | null; contactEmail: string | null; registrationNumber: string | null; taxNumber: string | null;
  itContactName: string | null; itContactEmail: string | null; documents: OrganisationDocument[]; history: HistoryEntry[];
};

/** The queue's filters (console design, 10 Nov 2026); the server maps each to states. */
export const filters = ['Waiting', 'NeedsChanges', 'Decided', 'All'] as const;
export type QueueFilter = (typeof filters)[number];
export const isFilter = (v: string | null): v is QueueFilter => filters.includes(v as QueueFilter);

/** The queue table's columns, in order. */
export const queueColumns = ['name', 'type', 'country', 'submitted', 'state'] as const;

export type Decision = 'approve' | 'request-changes' | 'reject' | 'suspend' | 'reinstate';

/** What a reviewer may do from each state (the server's state machine refuses anything else). */
export const decisionsFor = (state: OrganisationState): Decision[] =>
  state === 'Submitted' ? ['approve', 'request-changes', 'reject'] : state === 'Approved' ? ['suspend'] : state === 'Suspended' ? ['reinstate'] : [];

/** Operations and reviewers decide; support reads only (PlatformPolicies.OrganisationApprove). */
export const canDecide = (role: PlatformRole) => role === 'Operations' || role === 'Reviewer';

/** Approving and reinstating are the positive moves; the others are shown as destructive. */
export const isDestructive = (d: Decision) => d !== 'approve' && d !== 'reinstate';

/** A refused decision's message key: a hospital without its DPA, a move the state machine does not allow, or a generic failure. */
export const refusalKey = (fields: Record<string, string[]>) =>
  fields['document.Dpa'] ? 'platform.organisations.refused.dpa' as const
    : fields.state ? 'platform.organisations.refused.state' as const
      : 'platform.error.failed' as const;
