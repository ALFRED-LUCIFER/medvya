import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '../../schedule/services/scheduleApi';
import type { NoteDraft, NoteView, SavePhase } from '../domain/note';
import { createAutosaver } from './noteAutosaver';

const view = (version: number, sections: Record<string, string>): NoteView =>
  ({ encounterId: 'e-1', templateKey: 'general-opd', templateVersion: 1, status: 'Draft', manual: true, sections, version, updatedAt: null });
const draft = (sections: Record<string, string>): NoteDraft => ({ templateKey: 'general-opd', sections });
const stale = (current: number) => new ApiError(409, 'stale_note', { code: 'stale_note', currentVersion: current });

function setup(save: (d: NoteDraft, v: number) => Promise<NoteView>) {
  const phases: SavePhase[] = [];
  const saver = createAutosaver({ save, delayMs: 1000, onPhase: p => phases.push(p) });
  return { saver, phases };
}

beforeEach(() => vi.useFakeTimers());
afterEach(() => vi.useRealTimers());

describe('autosaver', () => {
  it('saves once, after the doctor stops typing, on the version it last saw', async () => {
    const save = vi.fn(async (d: NoteDraft, v: number) => view(v + 1, d.sections));
    const { saver, phases } = setup(save);
    saver.edit(draft({ plan: 'a' }));
    await vi.advanceTimersByTimeAsync(600);
    saver.edit(draft({ plan: 'ab' }));
    await vi.advanceTimersByTimeAsync(999);
    expect(save).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(1);
    expect(save).toHaveBeenCalledExactlyOnceWith(draft({ plan: 'ab' }), 0);
    expect(phases).toEqual(['saving', 'saved']);
    saver.edit(draft({ plan: 'abc' }));
    await vi.advanceTimersByTimeAsync(1000);
    expect(save).toHaveBeenLastCalledWith(draft({ plan: 'abc' }), 1);
  });

  it('keeps the text after a failed save and sends it with the next edit or a retry', async () => {
    const save = vi.fn().mockRejectedValueOnce(new TypeError('offline')).mockImplementation(async (d: NoteDraft, v: number) => view(v + 1, d.sections));
    const { saver, phases } = setup(save);
    saver.edit(draft({ plan: 'kept' }));
    await vi.advanceTimersByTimeAsync(1000);
    expect(phases).toEqual(['saving', 'failed']);
    expect(saver.unsaved()).toBe(true);
    await saver.retry();
    expect(save).toHaveBeenLastCalledWith(draft({ plan: 'kept' }), 0);
    expect(phases.at(-1)).toBe('saved');
    expect(saver.unsaved()).toBe(false);
  });

  it('on a stale save holds later saves, then reapplies the doctor\'s changes over the newer version', async () => {
    const save = vi.fn().mockRejectedValueOnce(stale(2)).mockImplementation(async (d: NoteDraft, v: number) => view(v + 1, d.sections));
    const { saver, phases } = setup(save);
    saver.setBase(view(1, { plan: 'old', assessment: 'A' }));
    saver.edit(draft({ plan: 'mine', assessment: 'A' }));
    await vi.advanceTimersByTimeAsync(1000);
    expect(phases).toEqual(['saving', 'conflict']);
    saver.edit(draft({ plan: 'mine more', assessment: 'A' })); // typing on during the conflict
    await vi.advanceTimersByTimeAsync(5000);
    expect(save).toHaveBeenCalledTimes(1); // nothing overwrites the server's note
    const merged = saver.resolve(view(2, { plan: 'old', assessment: 'theirs' }));
    expect(merged).toEqual(draft({ plan: 'mine more', assessment: 'theirs' })); // my plan edit kept, their assessment kept
    await vi.advanceTimersByTimeAsync(0);
    expect(save).toHaveBeenLastCalledWith(merged, 2);
    expect(phases.at(-1)).toBe('saved');
  });
});
