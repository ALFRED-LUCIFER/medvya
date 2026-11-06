import type { Cast } from '../../../dev/cast';

/** Release builds have no data until the API's native sign-in exists; screens show this as "not connected". */
export class NotConnectedError extends Error {
  constructor() { super('mobile.error.notConnected'); }
}

/**
 * The one place screens get data from.
 * shortcut: Development builds read the synthetic cast (src/dev/cast.ts); release builds refuse. Replace with
 * @medvya/api-client calls over native tokens when MDV-P5-033 lands.
 */
export async function fromCast<T>(pick: (cast: Cast) => T, dev: boolean = __DEV__): Promise<T> {
  if (!dev) throw new NotConnectedError();
  const cast = loadCast();
  await new Promise(r => setTimeout(r, 250)); // a network-like pause so loading states are exercised in development
  return pick(cast);
}

// Metro folds `if (__DEV__)` away in release builds before it collects dependencies, so the cast is not in a release bundle.
function loadCast(): Cast {
  if (__DEV__) return (require('../../../dev/cast') as { cast: Cast }).cast;
  throw new NotConnectedError();
}
