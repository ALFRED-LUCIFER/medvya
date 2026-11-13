// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest';
import { fetchNoteAfterLeaving, markLeftWithSave, saveNote } from './noteApi';

// MDV-P1-089: the save sent as the page goes away, and the read after a reload that waits for it. Synthetic text only.
const view = (version: number) => ({ encounterId: 'e-1', templateKey: 'general-opd', templateVersion: 1, status: 'Draft', manual: true, sections: {}, version, updatedAt: null });

afterEach(() => { vi.unstubAllGlobals(); sessionStorage.clear(); });

describe('note api on leaving the page', () => {
  it('sends a save with keepalive when asked', async () => {
    const fetch = vi.fn(async (_url: string, _init?: RequestInit) => Response.json(view(3)));
    vi.stubGlobal('fetch', fetch);
    await saveNote('e-1', { templateKey: 'general-opd', sections: { plan: 'Rest (synthetic)' }, recordingConsent: null }, 2, true);
    expect(fetch.mock.calls[0]![1]).toMatchObject({ method: 'PUT', keepalive: true });
  });

  it('after a save sent on leaving, reads again until the server holds it; storage keeps only the id and a version, then nothing', async () => {
    const versions = [2, 2, 3];
    const fetch = vi.fn(async () => Response.json(view(versions.shift()!)));
    vi.stubGlobal('fetch', fetch);
    markLeftWithSave('e-1', 2);
    expect(JSON.stringify(sessionStorage)).toBe('{"medvya.note.leftAt.e-1":"2"}'); // no note text
    expect((await fetchNoteAfterLeaving('e-1', 0)).version).toBe(3);
    expect(fetch).toHaveBeenCalledTimes(3);
    expect(sessionStorage.length).toBe(0);
  });

  it('without a save on leaving, reads once; a save that never lands is waited for ten times at most', async () => {
    const fetch = vi.fn(async () => Response.json(view(2)));
    vi.stubGlobal('fetch', fetch);
    await fetchNoteAfterLeaving('e-1', 0);
    expect(fetch).toHaveBeenCalledTimes(1);
    markLeftWithSave('e-1', 2);
    expect((await fetchNoteAfterLeaving('e-1', 0)).version).toBe(2);
    expect(fetch).toHaveBeenCalledTimes(1 + 11);
  });
});
