import { describe, expect, it } from 'vitest';
import { inviteForm, isReviewDue, keyState, linkUrl, reasonForm, type TeamMember } from './team';

const m = (over: Partial<TeamMember>): TeamMember => ({ id: '1', displayName: 'A', email: 'a@medvya.example', role: 'Support', hasKey: false, joined: false, lastActiveAt: null, accessEndsOn: null, ...over });

describe('team rules', () => {
  it('writes the key state out rather than leaving it to colour', () => {
    expect(keyState(m({ hasKey: true, joined: true }))).toBe('registered');
    expect(keyState(m({}))).toBe('invited');
    expect(keyState(m({ joined: true }))).toBe('none');
  });
  it('keeps the link token in the fragment', () => {
    expect(linkUrl('https://platform.medvya.example', 'tok')).toBe('https://platform.medvya.example/enrol#link=tok');
  });
  it('needs a reason of 5 to 500 characters', () => {
    expect(reasonForm.safeParse({ reason: '  ok  ' }).success).toBe(false);
    expect(reasonForm.safeParse({ reason: 'Quarterly review' }).success).toBe(true);
    expect(reasonForm.safeParse({ reason: 'x'.repeat(501) }).success).toBe(false);
  });
  it('accepts an invite with or without an end date', () => {
    const base = { displayName: 'Kavya (synthetic)', email: 'kavya@medvya.example', role: 'Reviewer', reason: 'New reviewer joining' } as const;
    expect(inviteForm.safeParse({ ...base, accessEndsOn: '' }).success).toBe(true);
    expect(inviteForm.safeParse({ ...base, accessEndsOn: '2027-02-01' }).success).toBe(true);
    expect(inviteForm.safeParse({ ...base, accessEndsOn: 'soon' }).success).toBe(false);
  });
  it('says the access review is due once its date has passed', () => {
    expect(isReviewDue({ members: [], lastAccessReviewAt: null, accessReviewDueAt: '2026-11-01T00:00:00Z' }, new Date('2026-11-07T00:00:00Z'))).toBe(true);
    expect(isReviewDue({ members: [], lastAccessReviewAt: null, accessReviewDueAt: '2027-02-01T00:00:00Z' }, new Date('2026-11-07T00:00:00Z'))).toBe(false);
  });
});
