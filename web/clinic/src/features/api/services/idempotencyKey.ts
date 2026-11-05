/** One Idempotency-Key per submission (MDV-P1-047): the server turns a repeat with the same key into the first answer, not a second record. */
export const newIdempotencyKey = () => crypto.randomUUID();

/** Kept in a `useRef` by the controller that submits. */
export type HeldKey = { current: { key: string; sig: string } | null };

/**
 * The key of this submission: the same while the request is unchanged (a retry after a timeout or a second tap), a new one when the
 * desk edited it (the server refuses one key for two different requests). Call `forgetKey` once the answer has arrived.
 */
export function keyFor(held: HeldKey, request: unknown): string {
  const sig = JSON.stringify(request);
  if (held.current?.sig !== sig) held.current = { key: newIdempotencyKey(), sig };
  return held.current.key;
}

export const forgetKey = (held: HeldKey) => { held.current = null; };
