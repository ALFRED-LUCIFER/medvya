import { describe, expect, it } from 'vitest';
import { canDecide, decisionsFor, isFilter, refusalKey } from './organisations';

describe('organisation review', () => {
  it('offers only the moves the state machine allows', () => {
    expect(decisionsFor('Submitted')).toEqual(['approve', 'request-changes', 'reject']);
    expect(decisionsFor('Approved')).toEqual(['suspend']);
    expect(decisionsFor('Suspended')).toEqual(['reinstate']);
    expect(decisionsFor('NeedsChanges')).toEqual([]);
    expect(decisionsFor('Rejected')).toEqual([]);
    expect(decisionsFor('Draft')).toEqual([]);
  });

  it('lets operations and reviewers decide, nobody else', () => {
    expect(canDecide('Operations')).toBe(true);
    expect(canDecide('Reviewer')).toBe(true);
    expect(canDecide('Support')).toBe(false);
    expect(canDecide('Owner')).toBe(false);
    expect(canDecide('Finance')).toBe(false);
  });

  it('reads the filter from the address and explains a refusal', () => {
    expect(isFilter('Decided')).toBe(true);
    expect(isFilter('Approved')).toBe(false);
    expect(isFilter(null)).toBe(false);
    expect(refusalKey({ 'document.Dpa': ['required'] })).toBe('platform.organisations.refused.dpa');
    expect(refusalKey({ state: ['transition'] })).toBe('platform.organisations.refused.state');
    expect(refusalKey({})).toBe('platform.error.failed');
  });
});
