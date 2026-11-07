import { z } from 'zod';
import type { PlatformRole } from '../../session/domain/session';

export const roles: readonly PlatformRole[] = ['Owner', 'Operations', 'Support', 'Finance', 'Reviewer'];

export type TeamMember = {
  id: string; displayName: string; email: string; role: PlatformRole; hasKey: boolean; joined: boolean;
  lastActiveAt: string | null; accessEndsOn: string | null;
};
export type Team = { members: TeamMember[]; lastAccessReviewAt: string | null; accessReviewDueAt: string };
export type IssuedLink = { memberId: string; token: string; expiresAt: string };

/** Key column: registered, invited but not joined, or joined with no live key (lost key, recovery pending). */
export const keyState = (m: TeamMember): 'registered' | 'invited' | 'none' => (m.hasKey ? 'registered' : m.joined ? 'none' : 'invited');

/** The link an owner shares (only after checking the person on a video call); the token stays in the fragment. */
export const linkUrl = (origin: string, token: string) => `${origin}/enrol#link=${token}`;

/** Every console action needs one; the server applies the same limits (PlatformReason). */
export const reasonSchema = z.string().trim().min(5, 'reason.short').max(500, 'reason.long');
export const reasonForm = z.object({ reason: reasonSchema });
export type ReasonForm = z.infer<typeof reasonForm>;

export const inviteForm = z.object({
  displayName: z.string().trim().min(1, 'required').max(100, 'tooLong'),
  email: z.email('email').max(254, 'tooLong'),
  role: z.enum(['Owner', 'Operations', 'Support', 'Finance', 'Reviewer']),
  /** YYYY-MM-DD, or empty for "until removed". */
  accessEndsOn: z.string().regex(/^(\d{4}-\d{2}-\d{2})?$/, 'date'),
  reason: reasonSchema,
});
export type InviteForm = z.infer<typeof inviteForm>;

export const isReviewDue = (team: Team, now: Date) => new Date(team.accessReviewDueAt) <= now;

/** What the reason form is for: recovering or removing one member, or recording the quarterly access review. */
export type PendingAction = { kind: 'recover' | 'remove'; memberId: string; name: string } | { kind: 'review' };

/** A link just issued, with its full address; shown once. */
export type ShownLink = IssuedLink & { url: string; purpose: 'invite' | 'recover' };
