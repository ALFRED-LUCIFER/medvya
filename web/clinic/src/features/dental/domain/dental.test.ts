import { describe, expect, it } from 'vitest';
import { byPhase, dentalErrors, errorKey, findingProblem, historyComplete, historyOnVisit, historyQuestions, marksFor, needsConsent, nextSteps, sortSurfaces, type PlanItem, type ToothFinding } from './dental';
import en from '../../../../../../packages/i18n/messages/en.json';

const item = (id: string, phase: number): PlanItem => ({
  id, serviceId: 's', serviceName: id, tooth: '36', surfaces: null, phase, estimateMinor: 100, currency: 'INR', status: 'Planned', reason: null,
  createdAt: '2026-11-06T04:30:00Z', changedAt: '2026-11-06T04:30:00Z', doneAt: null, version: 1, consentKey: null, consent: null,
});

describe('dental chart and plan (MDV-P8-011, MDV-P8-012)', () => {
  it('sends surfaces in the server\'s order, whatever order they were picked in', () => {
    expect(sortSurfaces('OD')).toBe('DO');
    expect(sortSurfaces(['L', 'M', 'O'])).toBe('MOL');
    expect(sortSurfaces('')).toBe('');
  });

  it('asks for a surface only on caries and fillings', () => {
    expect(findingProblem(null, '')).toBe('condition');
    expect(findingProblem('Caries', '')).toBe('surfaces:required');
    expect(findingProblem('Filling', 'O')).toBeNull();
    expect(findingProblem('Missing', '')).toBeNull();
  });

  it('offers only the steps the server allows: Done is final, a declined item can be offered again', () => {
    expect(nextSteps('Planned')).toEqual(['start', 'decline']);
    expect(nextSteps('InProgress')).toEqual(['done', 'decline']);
    expect(nextSteps('Declined')).toEqual(['offer']);
    expect(nextSteps('Done')).toEqual([]);
  });

  it('groups the plan by phase in phase order', () => {
    expect(byPhase([item('a', 2), item('b', 1), item('c', 2)]).map(g => [g.phase, g.items.map(i => i.id)])).toEqual([[1, ['b']], [2, ['a', 'c']]]);
  });

  it('marks a recorded tooth with its code and words, surfaces included', () => {
    const f: ToothFinding = { id: '1', tooth: '36', condition: 'Caries', surfaces: 'DO', note: null, encounterId: 'e', recordedBy: 'u', recordedAt: '', author: null };
    const t = (key: string) => (en as Record<string, string>)[key] ?? key;
    expect(marksFor([f], t as never)['36']).toEqual({ abbr: 'C', label: 'Caries (DO)', tone: 'danger', surfaces: 'DO', fill: 'hatch' });
  });

  it('has words for every error the dental routes answer, and none for an unknown one', () => {
    for (const code of dentalErrors) expect((en as Record<string, string>)[errorKey(code)!], code).toBeTruthy();
    expect(errorKey('version:stale')).toBeNull();
    expect(errorKey(undefined)).toBeNull();
  });
});

describe('the medical history check (MDV-P8-014)', () => {
  it('counts as confirmed only on the open visit, and needs every question answered', () => {
    const c = (encounterId: string) => ({ id: 'h', encounterId, yes: [], note: null, confirmedBy: 'u', confirmedAt: '2026-11-06T04:40:00Z', author: null });
    expect(historyOnVisit({ openEncounterId: 'e-9', historyCheck: c('e-9') })).toBe(true);
    expect(historyOnVisit({ openEncounterId: 'e-9', historyCheck: c('e-1') })).toBe(false); // an earlier visit's check
    expect(historyOnVisit({ openEncounterId: null, historyCheck: c('e-9') })).toBe(false);
    expect(historyOnVisit({ openEncounterId: 'e-9', historyCheck: null })).toBe(false);
    const all = Object.fromEntries(historyQuestions.map(q => [q, false]));
    expect(historyComplete(all)).toBe(true);
    expect(historyComplete({ ...all, Pregnancy: undefined })).toBe(false);
  });
});

describe('the procedure consent (MDV-P8-019)', () => {
  it('holds the item until a current consent is signed; a withdrawn or outdated one does not count', () => {
    const c = (current: boolean) => ({ id: 'c', planItemId: 'p', templateKey: 'extraction', templateVersion: 1, language: 'en', signedBy: 'x', signedAt: '2026-11-06T04:30:00Z', withdrawnAt: null, withdrawalReason: null, current });
    expect(needsConsent({ consentKey: null, consent: null })).toBe(false);
    expect(needsConsent({ consentKey: 'extraction', consent: null })).toBe(true);
    expect(needsConsent({ consentKey: 'extraction', consent: c(false) })).toBe(true);
    expect(needsConsent({ consentKey: 'extraction', consent: c(true) })).toBe(false);
  });
});
