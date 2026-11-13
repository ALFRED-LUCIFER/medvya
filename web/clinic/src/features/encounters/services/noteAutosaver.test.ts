import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '../../schedule/services/scheduleApi';
import type { NoteDraft, NoteView, SavePhase } from '../domain/note';
import { createAutosaver } from './noteAutosaver';

const view = (version: number, sections: Record<string, string>): NoteView =>
  ({ encounterId: 'e-1', templateKey: 'general-opd', templateVersion: 1, status: 'Draft', manual: true, sections, version, updatedAt: null });
const draft = (sections: Record<string, string>): NoteDraft => ({ templateKey: 'general-opd', sections, recordingConsent: null });
const stale = (current: number) => new ApiError(409, 'stale_note', { code: 'stale_note', currentVersion: current });

function setup(save: (d: NoteDraft, v: number, keepalive: boolean) => Promise<NoteView>) {
  const phases: SavePhase[] = [];
  const saved: NoteView[] = [];
  const saver = createAutosaver({ save, delayMs: 1000, retryMs: 5000, onPhase: p => phases.push(p), onSaved: v => saved.push(v) });
  return { saver, phases, saved };
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
    expect(save).toHaveBeenCalledExactlyOnceWith(draft({ plan: 'ab' }), 0, false);
    expect(phases).toEqual(['saving', 'saved']);
    saver.edit(draft({ plan: 'abc' }));
    await vi.advanceTimersByTimeAsync(1000);
    expect(save).toHaveBeenLastCalledWith(draft({ plan: 'abc' }), 1, false);
  });

  it('keeps the text after a failed save and sends it with the next edit or a retry', async () => {
    const save = vi.fn().mockRejectedValueOnce(new TypeError('offline')).mockImplementation(async (d: NoteDraft, v: number) => view(v + 1, d.sections));
    const { saver, phases } = setup(save);
    saver.edit(draft({ plan: 'kept' }));
    await vi.advanceTimersByTimeAsync(1000);
    expect(phases).toEqual(['saving', 'failed']);
    expect(saver.unsaved()).toBe(true);
    await saver.retry();
    expect(save).toHaveBeenLastCalledWith(draft({ plan: 'kept' }), 0, false);
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
    expect(save).toHaveBeenLastCalledWith(merged, 2, false);
    expect(phases.at(-1)).toBe('saved');
  });

  it('flush saves what is typed before signing and reports the version to sign', async () => {
    const save = vi.fn(async (d: NoteDraft, v: number) => view(v + 1, d.sections));
    const { saver } = setup(save);
    saver.setBase(view(4, {}));
    saver.edit(draft({ plan: 'typed just now' }));
    expect(await saver.flush()).toBe(true);
    expect(save).toHaveBeenCalledExactlyOnceWith(draft({ plan: 'typed just now' }), 4, false);
    expect(saver.version()).toBe(5);
    await vi.advanceTimersByTimeAsync(5000);
    expect(save).toHaveBeenCalledTimes(1); // the debounce timer does not save it again
  });

  it('flush waits for a save in flight, and says no when the text could not be saved', async () => {
    let release!: () => void;
    const save = vi.fn()
      .mockImplementationOnce(async (d: NoteDraft, v: number) => { await new Promise<void>(r => { release = r; }); return view(v + 1, d.sections); })
      .mockRejectedValue(new ApiError(500));
    const { saver } = setup(save);
    saver.edit(draft({ plan: 'a' }));
    await vi.advanceTimersByTimeAsync(1000);
    saver.edit(draft({ plan: 'ab' }));
    const flushed = saver.flush();
    release();
    expect(await flushed).toBe(false); // the second save failed: signing must not go ahead
    expect(saver.unsaved()).toBe(true);
  });

  it('flush says no during a conflict, and markStale keeps the text for the editor\'s reapply', async () => {
    const save = vi.fn(async (d: NoteDraft, v: number) => view(v + 1, d.sections));
    const { saver, phases } = setup(save);
    saver.setBase(view(1, { plan: 'old' }));
    saver.markStale(draft({ plan: 'old' }));
    expect(phases.at(-1)).toBe('conflict');
    expect(await saver.flush()).toBe(false);
    expect(saver.resolve(view(2, { plan: 'theirs' }))).toEqual(draft({ plan: 'theirs' }));
  });

  // MDV-P1-089: "Saved" only once the server answered; "Not saved. Trying again." really tries again; the page going away sends the unsent text.
  it('reports saved only with the server\'s answer, and never before it', async () => {
    let release!: () => void;
    const save = vi.fn(async (d: NoteDraft, v: number) => { await new Promise<void>(r => { release = r; }); return view(v + 1, d.sections); });
    const { saver, phases, saved } = setup(save);
    saver.edit(draft({ plan: 'a' }));
    await vi.advanceTimersByTimeAsync(60_000);
    expect(phases).toEqual(['saving']); // a slow link: still not saved
    expect(saved).toEqual([]);
    release();
    await vi.advanceTimersByTimeAsync(0);
    expect(saved).toEqual([view(1, { plan: 'a' })]);
    expect(phases).toEqual(['saving', 'saved']);
  });

  it('tries a failed save again by itself, and never a stale one', async () => {
    const save = vi.fn().mockRejectedValueOnce(new TypeError('offline')).mockRejectedValueOnce(new ApiError(503))
      .mockImplementationOnce(async (d: NoteDraft, v: number) => view(v + 1, d.sections)).mockRejectedValue(stale(9));
    const { saver, phases, saved } = setup(save);
    saver.edit(draft({ plan: 'kept' }));
    await vi.advanceTimersByTimeAsync(1000);
    expect(phases).toEqual(['saving', 'failed']);
    await vi.advanceTimersByTimeAsync(4999);
    expect(save).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(1);
    expect(phases).toEqual(['saving', 'failed', 'saving', 'failed']);
    expect(saved).toEqual([]);
    await vi.advanceTimersByTimeAsync(5000);
    expect(phases.at(-1)).toBe('saved');
    expect(save).toHaveBeenLastCalledWith(draft({ plan: 'kept' }), 0, false);
    saver.edit(draft({ plan: 'more' }));
    await vi.advanceTimersByTimeAsync(1000);
    expect(phases.at(-1)).toBe('conflict');
    await vi.advanceTimersByTimeAsync(60_000);
    expect(save).toHaveBeenCalledTimes(4); // a conflict waits for the doctor
  });

  it('leave sends the unsent text at once with keepalive, also while a save is in flight, and nothing when all is saved', async () => {
    const save = vi.fn(async (d: NoteDraft, v: number) => view(v + 1, d.sections));
    const { saver } = setup(save);
    saver.leave();
    expect(save).not.toHaveBeenCalled();
    saver.edit(draft({ plan: 'last words' }));
    saver.leave(); // inside the autosave delay
    expect(save).toHaveBeenCalledExactlyOnceWith(draft({ plan: 'last words' }), 0, true);
    await vi.advanceTimersByTimeAsync(5000);
    expect(save).toHaveBeenCalledTimes(1); // the debounce does not send it again

    let release!: () => void;
    const slow = vi.fn().mockImplementationOnce(async (d: NoteDraft, v: number) => { await new Promise<void>(r => { release = r; }); return view(v + 1, d.sections); })
      .mockImplementation(async (d: NoteDraft, v: number) => view(v + 1, d.sections));
    const second = setup(slow).saver;
    second.edit(draft({ plan: 'a' }));
    await vi.advanceTimersByTimeAsync(1000);
    second.edit(draft({ plan: 'ab' }));
    second.leave();
    expect(slow).toHaveBeenLastCalledWith(draft({ plan: 'ab' }), 0, true);
    release();
  });
});
