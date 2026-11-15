import type { Cast } from '../../../dev/cast';
// shortcut: duplicated in mobile/patient; both move to packages/auth-client with native tokens (MDV-P5-033).

/** A screen with no data source in this build (not yet wired to the API, or a release build without one): "not connected". */
export class NotConnectedError extends Error {
  constructor() { super('mobile.error.notConnected'); }
}

/**
 * The source switch: the Medvya server this build talks to (EXPO_PUBLIC_MEDVYA_API, inlined at build time), or null.
 * With a server, sign-in and Today use it (api.ts); without one, Development builds read the synthetic cast.
 */
export const apiBase = () => process.env.EXPO_PUBLIC_MEDVYA_API || null;

/**
 * Synthetic data for Development builds without a server (src/dev/cast.ts). Release builds, and any build with a server,
 * refuse: a screen not yet wired to the API says "not connected" instead of mixing made-up patients with real ones.
 */
export async function fromCast<T>(pick: (cast: Cast) => T, dev: boolean = __DEV__): Promise<T> {
  if (!dev || apiBase()) throw new NotConnectedError();
  const cast = loadCast();
  await new Promise(r => setTimeout(r, 250)); // a network-like pause so loading states are exercised in development
  return pick(cast);
}

// Metro folds `if (__DEV__)` away in release builds before it collects dependencies, so the cast is not in a release bundle.
function loadCast(): Cast {
  if (__DEV__) return (require('../../../dev/cast') as { cast: Cast }).cast;
  throw new NotConnectedError();
}
