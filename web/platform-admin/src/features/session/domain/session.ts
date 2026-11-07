export type PlatformRole = 'Owner' | 'Operations' | 'Support' | 'Finance' | 'Reviewer';

/** GET /v1/me: who signed on, and how far: company sign-on alone, or the security key too. */
export type Me = { displayName: string; role: PlatformRole; keyVerified: boolean; hasKey: boolean; canRegister: boolean };

/**
 * signedOut: company sign-on first. registerKey: this session holds an owner's one-hour link (or is the first owner's first key).
 * touchKey: the member has a key to touch. askOwner: no key and no link, so another owner must issue one. in: the console is open.
 */
export type Stage = 'signedOut' | 'registerKey' | 'touchKey' | 'askOwner' | 'in';

export const stageOf = (me: Me | null): Stage =>
  !me ? 'signedOut' : me.keyVerified ? 'in' : me.canRegister ? 'registerKey' : me.hasKey ? 'touchKey' : 'askOwner';

/** The token of an owner's key link, from the page's fragment (#link=…): a fragment never reaches a server log. */
export const linkFrom = (hash: string): string | null => /^#link=([A-Za-z0-9_-]{20,128})$/.exec(hash)?.[1] ?? null;
