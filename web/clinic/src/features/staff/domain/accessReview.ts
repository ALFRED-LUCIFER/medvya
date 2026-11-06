import type { Role } from '../../session/domain/session';

// GET /v1/staff/access-review (IAccessReviews + names and last sign-in added by the host). Staff only; no patient data.
export type ReviewDecision = 'Confirmed' | 'Offboarded';
export type ReviewMember = { membershipId: string; name: string | null; role: Role; facilityName: string; lastSignIn: string | null; decision: ReviewDecision | null };
export type AccessReview = { quarter: string; completedAt: string | null; lastCompletedAt: string | null; members: ReviewMember[] };

/** People still waiting for a decision this quarter. */
export const remaining = (review: Pick<AccessReview, 'members'>) => review.members.filter(m => m.decision === null).length;
