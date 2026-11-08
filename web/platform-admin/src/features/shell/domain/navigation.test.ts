import { describe, expect, it } from 'vitest';
import { navFor } from './navigation';

describe('navFor', () => {
  it('shows team access to owners only and the audit log to owners and operations', () => {
    expect(navFor('Owner').map(i => i.id)).toEqual(['overview', 'team', 'audit']);
    expect(navFor('Operations').map(i => i.id)).toEqual(['overview', 'audit']);
    expect(navFor('Reviewer').map(i => i.id)).toEqual(['overview']);
  });
});
