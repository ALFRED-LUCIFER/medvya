// Mirrors src/Modules/Patients (IPatientMerges); the server is the authority.
import type { IdentifierScheme, SexAtBirth } from '../../patients/domain/directory';

export type MergeStatus = 'Proposed' | 'NotSame' | 'AwaitingApproval' | 'Merged' | 'Unmerged';

export type MergeQueueRow = { id: string; status: MergeStatus; survivorMrn: string; mergedMrn: string; decidedAt: string };

export type MergeSide = {
  patientId: string; fullName: string; mrn: string; dateOfBirth: string; sexAtBirth: SexAtBirth; mobile: string;
  identifiers: { scheme: IdentifierScheme; lastFour: string }[]; visits: number;
};

export type MergeReview = {
  id: string; status: MergeStatus; survivor: MergeSide; merged: MergeSide; approvalsRequired: number; approvals: number; canUndo: boolean;
};

export type MergeHistoryEntry = { id: string; status: MergeStatus; thisIsSurvivor: boolean; otherMrn: string; mergedAt: string | null; unmergedAt: string | null };

export type MergeAction = 'notSame' | 'requestApproval' | 'approve' | 'undo';

/** What each status still allows: the server enforces it, the screen only offers what can work. */
export function availableActions(review: Pick<MergeReview, 'status' | 'canUndo'>, can: { request: boolean; approve: boolean; undo: boolean }): MergeAction[] {
  const { status } = review;
  return [
    ...(can.request && (status === 'Proposed' || status === 'AwaitingApproval') ? ['notSame' as const] : []),
    ...(can.request && status === 'Proposed' ? ['requestApproval' as const] : []),
    ...(can.approve && status === 'AwaitingApproval' ? ['approve' as const] : []),
    ...(can.undo && status === 'Merged' && review.canUndo ? ['undo' as const] : []),
  ];
}

const refusals = [
  'same_record', 'already_merged', 'pair_decided', 'not_open', 'not_proposed', 'not_awaiting_approval', 'same_approver', 'record_in_merge',
  'not_merged', 'undo_window_over',
] as const;

export type Refusal = (typeof refusals)[number] | 'http403' | 'http404' | 'http500' | 'unknown';

/** The server's refusal code or HTTP status as a message key suffix; anything unrecognised is the generic message. */
export function refusalOf(code: string | undefined, status: number): Refusal {
  const known = refusals.find(r => r === code);
  return known ?? (status === 403 ? 'http403' : status === 404 ? 'http404' : status >= 500 ? 'http500' : 'unknown');
}
