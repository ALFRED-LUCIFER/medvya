import { describe, expect, it } from 'vitest';
import { lastUsedFirst, nextSession, toSession, type MeContext } from './session';
import { signInSchema } from './signIn';

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

describe('lastUsedFirst and toSession (MDV-P1-059)', () => {
  const m = (id: string) => ({ id, tenantId: 't', tenantName: 'Iyer Family Clinic', facilityId: `f-${id}`, facilityName: id, role: 'Receptionist' as const });
  const [adyar, besant, velachery] = [m('adyar'), m('besant'), m('velachery')];

  it('moves the last used membership to the top and keeps the rest in server order', () => {
    expect(lastUsedFirst([adyar, besant, velachery], 'velachery').map(x => x.id)).toEqual(['velachery', 'adyar', 'besant']);
  });

  it('keeps the server order when nothing was used before or the id is not a membership', () => {
    expect(lastUsedFirst([adyar, besant], null).map(x => x.id)).toEqual(['adyar', 'besant']);
    expect(lastUsedFirst([adyar, besant], 'gone').map(x => x.id)).toEqual(['adyar', 'besant']);
  });

  it('carries the order and the id into the session', () => {
    const s = toSession({ ...me, memberships: [adyar, besant], lastMembershipId: 'besant' });
    expect(s.memberships.map(x => x.id)).toEqual(['besant', 'adyar']);
    expect(s.lastMembershipId).toBe('besant');
    expect(toSession(me).lastMembershipId).toBeNull();
  });
});

describe('signInSchema', () => {
  it('accepts a work email and refuses anything else', () => {
    expect(signInSchema.safeParse({ email: 'reception.desk@example.test' }).success).toBe(true);
    for (const email of ['', 'reception', 'a b@example.test', `${'a'.repeat(250)}@example.test`])
      expect(signInSchema.safeParse({ email }).success, email).toBe(false);
  });
});
