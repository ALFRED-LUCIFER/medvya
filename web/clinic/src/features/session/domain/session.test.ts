import { describe, expect, it } from 'vitest';
import { endingSoon, lastUsedFirst, nextPollMs, nextSession, toSession, type MeContext, type SessionEnd } from './session';
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

describe('the end of a session (#1365 F-057)', () => {
  const end = (endsInSeconds: number, extendable = true, idleTimeoutSeconds = 900): SessionEnd => ({ endsInSeconds, extendable, idleTimeoutSeconds });

  it('gives notice two minutes before the end, or half the time-out where a clinic set a very short one', () => {
    expect([121, 120, 0].map(s => endingSoon(end(s)))).toEqual([false, true, true]);
    expect([31, 30].map(s => endingSoon(end(s, true, 60)))).toEqual([false, true]);
    expect([endingSoon(null), endingSoon(undefined)]).toEqual([false, false]); // an older server says nothing: no notice, never a guess
  });

  it('asks again each minute, in time for the notice, and right after the end once the notice shows', () => {
    expect(nextPollMs(undefined)).toBe(60_000);
    expect(nextPollMs(end(900))).toBe(60_000);
    expect(nextPollMs(end(150))).toBe(30_000);  // the notice is due in 30 s
    expect(nextPollMs(end(122))).toBe(5_000);   // never under five seconds apart
    expect(nextPollMs(end(40))).toBe(41_000);   // showing: look again just after the end
    expect(nextPollMs(end(110))).toBe(60_000);
  });

  it('keeps the end beside the context, so the context keeps its shape whatever the clock says', () => {
    expect(nextSession(true, { ...me, session: end(100) })).toEqual({ kind: 'signedIn', me, end: end(100) });
    expect(nextSession(true, me)).toEqual({ kind: 'signedIn', me });
  });

  it('says a session ended for lack of use only when the idle notice was showing', () => {
    expect(nextSession(true, null, { kind: 'signedIn', me, end: end(30) })).toEqual({ kind: 'expired', idle: true });
    expect(nextSession(true, null, { kind: 'signedIn', me, end: end(600) })).toEqual({ kind: 'expired' }); // revoked or signed out elsewhere
    expect(nextSession(true, null, { kind: 'signedIn', me, end: end(30, false) })).toEqual({ kind: 'expired' }); // the 8-hour limit, not idleness
    expect(nextSession(true, null, { kind: 'signedIn', me })).toEqual({ kind: 'expired' });
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

  it('knows access ended only when the server says so (F-05b); older servers mean "never had a clinic"', () => {
    expect(toSession({ ...me, accessEnded: true }).accessEnded).toBe(true);
    expect(toSession(me).accessEnded).toBe(false);
  });
});

describe('signInSchema', () => {
  it('accepts a work email and refuses anything else', () => {
    expect(signInSchema.safeParse({ email: 'reception.desk@example.test' }).success).toBe(true);
    for (const email of ['', 'reception', 'a b@example.test', `${'a'.repeat(250)}@example.test`])
      expect(signInSchema.safeParse({ email }).success, email).toBe(false);
  });
});
