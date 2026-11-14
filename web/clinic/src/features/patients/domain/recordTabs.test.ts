import { describe, expect, it } from 'vitest';
import { recordTabs } from './recordTabs';

const tabs = (access: Parameters<typeof recordTabs>[1]) => recordTabs('p-1', access).map(t => t.tab);

describe('Patient record tabs (MDV-P2-051)', () => {
  it('gives a doctor Summary first and every tab, at the existing addresses', () => {
    expect(recordTabs('p-1', { clinical: true, details: true, documents: true })).toEqual([
      { tab: 'summary', path: '/patients/p-1' }, { tab: 'details', path: '/patients/directory/p-1' },
      { tab: 'timeline', path: '/patients/p-1/timeline' }, { tab: 'documents', path: '/patients/p-1/docs' },
    ]);
  });

  it('gives the front desk Details first and nothing clinical', () => {
    expect(tabs({ clinical: false, details: true, documents: true })).toEqual(['details', 'documents']);
    expect(tabs({ clinical: false, details: true, documents: false })).toEqual(['details']); // practice admin: no desk documents
  });
});
