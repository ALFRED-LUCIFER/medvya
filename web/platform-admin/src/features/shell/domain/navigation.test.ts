import { describe, expect, it } from 'vitest';
import { navFor } from './navigation';

describe('navFor', () => {
  it('shows team access to owners only, the audit log to owners and operations, and organisations to those who review them', () => {
    expect(navFor('Owner').map(i => i.id)).toEqual(['overview', 'team', 'audit']);
    expect(navFor('Operations').map(i => i.id)).toEqual(['overview', 'organisations', 'audit']);
    expect(navFor('Reviewer').map(i => i.id)).toEqual(['overview', 'organisations']);
    expect(navFor('Support').map(i => i.id)).toEqual(['overview', 'organisations']);
    expect(navFor('Finance').map(i => i.id)).toEqual(['overview']);
  });
});
