import { describe, expect, it } from 'vitest';
import { nextSession, type MeContext } from './session';

const me: MeContext = { user: { id: 'u-joseph', displayName: 'A. Joseph (synthetic)' }, memberships: [], active: null };

describe('nextSession', () => {
  it('is signed in whenever the server returns a context', () => {
    expect(nextSession(false, me)).toEqual({ kind: 'signedIn', me });
    expect(nextSession(true, me)).toEqual({ kind: 'signedIn', me });
  });

  it('reports a session that ended under this tab as expired (F-05a), not as a plain sign-out', () => {
    expect(nextSession(true, null)).toEqual({ kind: 'expired' });
  });

  it('is signed out when this tab never had a session (or signed out)', () => {
    expect(nextSession(false, null)).toEqual({ kind: 'signedOut' });
  });
});
