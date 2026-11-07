import { describe, expect, it } from 'vitest';
import { linkFrom, stageOf, type Me } from './session';

const me = (over: Partial<Me>): Me => ({ displayName: 'Ops Admin (synthetic)', role: 'Owner', keyVerified: false, hasKey: true, canRegister: false, ...over });

describe('stageOf', () => {
  it('opens the console only once the key is verified', () => {
    expect(stageOf(null)).toBe('signedOut');
    expect(stageOf(me({}))).toBe('touchKey');
    expect(stageOf(me({ keyVerified: true }))).toBe('in');
  });
  it('registers a key when the session holds a link, even if an old key exists', () => {
    expect(stageOf(me({ canRegister: true }))).toBe('registerKey');
    expect(stageOf(me({ canRegister: true, hasKey: false }))).toBe('registerKey');
  });
  it('sends a member with no key and no link to another owner', () => {
    expect(stageOf(me({ hasKey: false }))).toBe('askOwner');
  });
});

describe('linkFrom', () => {
  it('reads only a well-formed link token from the fragment', () => {
    expect(linkFrom('#link=abcdefghijklmnopqrstuvwxyz_-0123456789')).toBe('abcdefghijklmnopqrstuvwxyz_-0123456789');
    expect(linkFrom('#link=short')).toBeNull();
    expect(linkFrom('#link=abc<script>defghijklmnopqrstu')).toBeNull();
    expect(linkFrom('')).toBeNull();
  });
});
