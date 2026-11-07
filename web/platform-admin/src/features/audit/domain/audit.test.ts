import { describe, expect, it } from 'vitest';
import en from '../../../../../../packages/i18n/messages/en.json';
import { actionKey, isKnownAction, knownActions } from './audit';

describe('audit actions', () => {
  it('has words for every action the console writes', () => {
    for (const a of knownActions) expect(en).toHaveProperty([actionKey(a)]);
  });
  it('treats an unknown code as unknown', () => {
    expect(isKnownAction('team.invite')).toBe(true);
    expect(isKnownAction('patient.read')).toBe(false);
  });
});
