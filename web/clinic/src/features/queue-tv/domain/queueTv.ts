import { z } from 'zod';

// GET /v1/queue/tv (src/Modules/Scheduling/Medvya.Scheduling.Application/IQueueService.cs). Tokens, rooms, doctor display names
// and waits only: never a patient's name, initials, MRN or reason.
export type QueueTv = {
  now: { token: string; room: string | null; doctor: string }[];
  next: { token: string; waitMinutes: number }[];
};

/** A display token from the link's fragment: base64url of 16 tenant bytes and 32 random bytes (64 characters). */
export const pairingToken = (fragment: string) => {
  const token = fragment.replace(/^#/, '');
  return /^[A-Za-z0-9_-]{64}$/.test(token) ? token : null;
};

/** Tokens called in since the previous view, for the chime; none on the first view, so opening the screen is silent. */
export const newlyCalled = (before: QueueTv | undefined, after: QueueTv) =>
  before ? after.now.map(n => n.token).filter(t => !before.now.some(b => b.token === t)) : [];

/** Why the screen cannot show the queue: a link that is wrong or revoked, or a screen that was never set up. */
export type Unpaired = 'invalid' | 'notSetUp';

// Displays (staff): GET/POST/DELETE /v1/queue/displays.
export type Display = { id: string; facilityId: string; name: string; createdAt: string; revokedAt: string | null };
export type CreatedDisplay = { display: Display; token: string };

export const displaySchema = z.object({ name: z.string().trim().min(1).max(60) });
export type DisplayInput = z.infer<typeof displaySchema>;
