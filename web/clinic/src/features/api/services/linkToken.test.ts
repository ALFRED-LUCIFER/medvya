// @vitest-environment jsdom
import { afterEach, describe, expect, it } from 'vitest';
import { takeLinkToken } from './linkToken';

afterEach(() => window.history.replaceState(null, '', '/'));

describe('takeLinkToken', () => {
  it('takes the token from the fragment and drops it from the address bar', () => {
    window.history.replaceState(null, '', '/invite?x=1#t=abc_DEF-123');
    expect(takeLinkToken()).toBe('abc_DEF-123');
    expect(window.location.hash).toBe('');
    expect(window.location.search).toBe('?x=1');
    expect(takeLinkToken()).toBe('abc_DEF-123'); // a later render of the same page keeps it
  });

  it('gives nothing on another page or for a fragment that is not a token', () => {
    window.history.replaceState(null, '', '/invite#t=abc');
    takeLinkToken();
    window.history.replaceState(null, '', '/rx');
    expect(takeLinkToken()).toBe('');
    window.history.replaceState(null, '', '/rx#t=<script>');
    expect(takeLinkToken()).toBe('');
  });
});
