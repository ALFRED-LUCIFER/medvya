import { describe, expect, it } from 'vitest';
import { navFor } from './navigation';

describe('navFor', () => {
  it('shows approvals and plans to every role, team access to owners only and the audit log to owners and operations', () => {
    expect(navFor('Owner').map(i => i.id)).toEqual(['overview', 'approvals', 'plans', 'team', 'audit']);
    expect(navFor('Operations').map(i => i.id)).toEqual(['overview', 'approvals', 'plans', 'audit']);
    expect(navFor('Reviewer').map(i => i.id)).toEqual(['overview', 'approvals', 'plans']);
    expect(navFor('Support').map(i => i.id)).toEqual(['overview', 'approvals', 'plans']);
    expect(navFor('Finance').map(i => i.id)).toEqual(['overview', 'approvals', 'plans']);
  });
});
