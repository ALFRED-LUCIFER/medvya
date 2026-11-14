import type { PlatformRole } from '../../session/domain/session';

export type OrgState = 'Draft' | 'Submitted' | 'NeedsChanges' | 'Approved' | 'Rejected' | 'Suspended';
export type OrgType = 'Clinic' | 'Hospital';
export type Filter = 'Waiting' | 'NeedsChanges' | 'Decided' | 'All';
export const filters: readonly Filter[] = ['Waiting', 'NeedsChanges', 'Decided', 'All'];

/** Column definitions are data: each is its heading's i18n key, and the first identifies a row (MDV-P2-054). */
export const queueColumns = ['platform.approvals.col.organisation', 'platform.approvals.col.state', 'platform.approvals.col.wait'] as const;
export const documentColumns = ['platform.approvals.docs.document', 'platform.approvals.docs.checks'] as const;
export const historyColumns = ['platform.audit.when', 'platform.approvals.history.decision', 'platform.audit.who', 'platform.audit.reason'] as const;

/** What an operator may do to an organisation; each is a POST to /v1/organisations/{id}/{action}. */
export type Action = 'approve' | 'request-changes' | 'reject' | 'suspend' | 'reinstate';

export type DocumentView = { id: string; kind: string; contentType: string; sizeBytes: number; uploadedAt: string };
export type QueueItem = {
  tenantId: string; name: string; country: string; type: OrgType; state: OrgState; submittedAt: string | null; reason: string | null;
  itContactName: string | null; itContactEmail: string | null; documents: DocumentView[];
};
export type HistoryEntry = { at: string; actor: string | null; state: string; reason: string | null };
export type Detail = {
  name: string; country: string; type: OrgType; state: OrgState; submittedAt: string | null; reason: string | null; address: string | null; contactEmail: string | null;
  registrationNumber: string | null; taxNumber: string | null; itContactName: string | null; itContactEmail: string | null; documents: DocumentView[]; history: HistoryEntry[];
};
export type EmailPreview = { subject: string; body: string };

/** Owner, operations and reviewer decide; support and finance only read (the server answers 403 to them, the buttons are simply absent). */
export const canDecide = (role: PlatformRole) => role === 'Owner' || role === 'Operations' || role === 'Reviewer';

/** The moves the state machine offers from a state (ADR-016); the server enforces the same table. */
export const actionsFor = (state: OrgState): readonly Action[] =>
  state === 'Submitted' ? ['approve', 'request-changes', 'reject'] : state === 'Approved' ? ['suspend'] : state === 'Suspended' ? ['reinstate'] : [];

export const hasDpa = (d: Pick<Detail, 'documents'>) => d.documents.some(x => x.kind === 'Dpa');

/** A hospital cannot be approved without its signed data processing agreement; the server refuses too (document.Dpa:required). */
export const approveBlockedByDpa = (d: Pick<Detail, 'type' | 'documents'>) => d.type === 'Hospital' && !hasDpa(d);

/** Actions that tell the applicant something they must act on or accept as final or as a pause: the reason is the message. */
export const isDestructive = (a: Action) => a === 'reject' || a === 'suspend';

export const documentKinds = ['RegistrationCertificate', 'HospitalRegistration', 'TaxRegistration', 'OwnerId', 'MedicalCouncilRegistration', 'SignatoryLetter', 'Dpa'] as const;
export const isDocumentKind = (k: string): k is (typeof documentKinds)[number] => (documentKinds as readonly string[]).includes(k);

/**
 * The checks that are cheap and certain: the file's type and size (the server read the type from the bytes) and the tax number's format.
 * shortcut: "name and address match" the documents needs reading them (OCR), which is not built; the screen says a person reads each document.
 * Add it with the document-AI work when counsel has agreed what Medvya may extract from an applicant's identity documents.
 */
export type TaxCheck = 'ok' | 'format' | 'missing';
const gstin = /^(0[1-9]|[1-2]\d|3[0-8])[A-Z]{5}\d{4}[A-Z][1-9A-Z]Z[0-9A-Z]$/;
const trn = /^\d{15}$/;
export function taxCheck(country: string, taxNumber: string | null): TaxCheck {
  const value = taxNumber?.replace(/\s/g, '').toUpperCase();
  if (!value) return 'missing';
  return (country === 'IN' ? gstin : country === 'AE' ? trn : /.+/).test(value) ? 'ok' : 'format';
}

export const sizeLabel = (bytes: number) => (bytes < 1024 * 1024 ? `${Math.max(1, Math.round(bytes / 1024))} KB` : `${(bytes / (1024 * 1024)).toFixed(1)} MB`);

/** Whole days since the request was submitted (0 when never). */
export const waitDays = (submittedAt: string | null, now: Date) =>
  submittedAt ? Math.max(0, Math.floor((now.getTime() - new Date(submittedAt).getTime()) / 86_400_000)) : 0;

export const decisionOf = (a: Action): OrgState => (a === 'approve' || a === 'reinstate' ? 'Approved' : a === 'request-changes' ? 'NeedsChanges' : a === 'reject' ? 'Rejected' : 'Suspended');

/** Server codes the screen words: state:transition (somebody decided first) and document.Dpa:required. */
export const errorKeyOf = (fields: Record<string, string[]>): 'state' | 'dpa' | null =>
  fields.state?.includes('transition') ? 'state' : fields['document.Dpa']?.includes('required') ? 'dpa' : null;
