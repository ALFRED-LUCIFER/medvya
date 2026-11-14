import { afterEach, describe, expect, it, vi } from 'vitest';
import { readFrontDeskView, saveFrontDeskView } from './viewPreference';

const store = new Map<string, string>();
const memory = { getItem: (k: string) => store.get(k) ?? null, setItem: (k: string, v: string) => void store.set(k, v) };

describe('the front desk view each receptionist chose last (MDV-P2-052)', () => {
  afterEach(() => { store.clear(); vi.unstubAllGlobals(); });

  it('opens the board until she chooses, then her own last choice, whoever else uses the desk', () => {
    vi.stubGlobal('localStorage', memory);
    expect(readFrontDeskView('u-1')).toBe('board');
    saveFrontDeskView('u-1', 'list');
    saveFrontDeskView('u-2', 'doctors');
    expect([readFrontDeskView('u-1'), readFrontDeskView('u-2'), readFrontDeskView('u-3')]).toEqual(['list', 'doctors', 'board']);
    expect([...store.values()]).toEqual(['list', 'doctors']); // a view name only, nothing about a patient
  });

  it('ignores a value it does not know and works with storage blocked', () => {
    vi.stubGlobal('localStorage', memory);
    store.set('medvya.frontDesk.view.u-1', 'grid');
    expect(readFrontDeskView('u-1')).toBe('board');
    vi.stubGlobal('localStorage', { getItem: () => { throw new Error('blocked'); }, setItem: () => { throw new Error('blocked'); } });
    expect(() => saveFrontDeskView('u-1', 'list')).not.toThrow();
    expect(readFrontDeskView('u-1')).toBe('board');
  });
});
