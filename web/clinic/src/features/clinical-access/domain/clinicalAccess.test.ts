import { describe, expect, it } from 'vitest';
import { refusalOf } from './clinicalAccess';

describe('refusalOf', () => {
  it('reads whether asking for access can help and whether a request waits', () => {
    expect(refusalOf({ code: 'no_care_relationship', requestPending: true })).toEqual({ canRequest: true, requestPending: true });
    expect(refusalOf({ code: 'no_care_relationship' })).toEqual({ canRequest: true, requestPending: false });
    expect(refusalOf({ code: 'no_access', requestPending: false })).toEqual({ canRequest: false, requestPending: false });
  });

  it('is null for any other answer', () => {
    for (const body of [null, undefined, 'no', {}, { code: 'not_today' }, { title: 'No facility context' }]) expect(refusalOf(body)).toBeNull();
  });
});
