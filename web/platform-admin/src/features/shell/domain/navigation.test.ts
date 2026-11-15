import { describe, expect, it } from 'vitest';
import { navFor } from './navigation';

describe('navFor', () => {
  it('shows approvals, features and plans to every role, team access to owners only and the audit log to owners and operations', () => {
    expect(navFor('Owner').map(i => i.id)).toEqual(['overview', 'approvals', 'features', 'plans', 'team', 'audit']);
    expect(navFor('Operations').map(i => i.id)).toEqual(['overview', 'approvals', 'features', 'plans', 'audit']);
    expect(navFor('Reviewer').map(i => i.id)).toEqual(['overview', 'approvals', 'features', 'plans']);
    expect(navFor('Support').map(i => i.id)).toEqual(['overview', 'approvals', 'features', 'plans']);
    expect(navFor('Finance').map(i => i.id)).toEqual(['overview', 'approvals', 'features', 'plans']);
  });
});
