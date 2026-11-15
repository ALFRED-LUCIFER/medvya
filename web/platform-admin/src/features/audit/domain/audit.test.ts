import { describe, expect, it } from 'vitest';
import en from '../../../../../../packages/i18n/messages/en.json';
import { actionKey, documentKindOf, isKnownAction, knownActions } from './audit';

describe('audit actions', () => {
  it('has words for every action the console writes', () => {
    for (const a of knownActions) expect(en).toHaveProperty([actionKey(a)]);
  });
  it('reads the document kind of a detail, also for a file kept aside from a rejected request', () => {
    expect(documentKindOf('TaxRegistration')).toBe('TaxRegistration');
    expect(documentKindOf('old-1a2b3c4d-OwnerId')).toBe('OwnerId');
  });
  it('treats an unknown code as unknown', () => {
    expect(isKnownAction('team.invite')).toBe(true);
    expect(isKnownAction('patient.read')).toBe(false);
  });
});
