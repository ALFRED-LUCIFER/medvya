export type PlatformRole = 'Owner' | 'Operations' | 'Support' | 'Finance' | 'Reviewer';

/** GET /v1/me: who signed on, and how far: company sign-on alone, or the security key too. */
export type Me = { displayName: string; email: string; role: PlatformRole; keyVerified: boolean; hasKey: boolean; canRegister: boolean; otherOwner: boolean };

/**
 * Why the key step failed. synced: the server refused a passkey that syncs between devices (key:synced). failed: anything else, a cancelled
 * prompt included; the browser does not say which.
 */
export type KeyProblem = 'synced' | 'failed';
export const keyProblemOf = (refusedFields: Record<string, string[]> | null): KeyProblem => (refusedFields?.key?.includes('synced') ? 'synced' : 'failed');

/**
 * signedOut: company sign-on first. registerKey: this session holds an owner's one-hour link (or is the first owner's first key).
 * touchKey: the member has a key to touch. askOwner: no key and no link, so another owner must issue one. in: the console is open.
 */
export type Stage = 'signedOut' | 'registerKey' | 'touchKey' | 'askOwner' | 'in';

export const stageOf = (me: Me | null): Stage =>
  !me ? 'signedOut' : me.keyVerified ? 'in' : me.canRegister ? 'registerKey' : me.hasKey ? 'touchKey' : 'askOwner';

/** The token of an owner's key link, from the page's fragment (#link=…): a fragment never reaches a server log. */
export const linkFrom = (hash: string): string | null => /^#link=([A-Za-z0-9_-]{20,128})$/.exec(hash)?.[1] ?? null;
