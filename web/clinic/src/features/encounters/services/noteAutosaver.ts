import { ApiError } from '../../schedule/services/scheduleApi';
import { reapply, staleVersion, type NoteDraft, type NoteSections, type NoteView, type SavePhase } from '../domain/note';

type Options = { save: (draft: NoteDraft, baseVersion: number) => Promise<NoteView>; delayMs: number; onPhase: (phase: SavePhase) => void };

/**
 * Debounced autosave of one note, in memory only (no PHI in browser storage). One save at a time: text typed while one is in flight goes in the
 * next. A failed save keeps the text and retries on the next edit or `retry()`. A stale save (409) holds every later save until `resolve()`,
 * so the doctor's text is never overwritten on the server and never lost here.
 */
export function createAutosaver({ save, delayMs, onPhase }: Options) {
  let base = { version: 0, sections: {} as NoteSections };
  let pending: NoteDraft | null = null;
  let phase: SavePhase = 'idle';
  let timer: ReturnType<typeof setTimeout> | undefined;
  let inflight: Promise<void> = Promise.resolve();
  const set = (p: SavePhase) => { phase = p; onPhase(p); };

  function run() {
    clearTimeout(timer);
    if (phase === 'saving' || phase === 'conflict' || !pending) return Promise.resolve();
    const draft = pending;
    pending = null;
    set('saving');
    return (inflight = attempt(draft));
  }

  async function attempt(draft: NoteDraft) {
    try {
      const view = await save(draft, base.version);
      base = { version: view.version, sections: view.sections };
      if (pending) void run(); else set('saved');
    } catch (e) {
      pending ??= draft;
      set(e instanceof ApiError && staleVersion(e.status, e.body) !== null ? 'conflict' : 'failed');
    }
  }

  return {
    /** The note as the server last had it (on load). */
    setBase(view: NoteView) { base = { version: view.version, sections: view.sections }; },
    edit(draft: NoteDraft) { pending = draft; clearTimeout(timer); if (phase !== 'conflict') timer = setTimeout(() => void run(), delayMs); },
    retry: run,
    /** Lays the unsaved text over the server's newer version, saves that, and returns it for the editor to show. */
    resolve(theirs: NoteView): NoteDraft | null {
      if (!pending) return null;
      const merged = { templateKey: pending.templateKey, sections: reapply(base.sections, pending.sections, theirs.sections) };
      base = { version: theirs.version, sections: theirs.sections };
      pending = merged;
      phase = 'idle';
      void run();
      return merged;
    },
    /** Before signing: saves what is typed and waits for a save in flight. False when it could not be saved (failed or in conflict). */
    async flush() {
      for (let i = 0; i < 3 && (pending || phase === 'saving') && phase !== 'conflict'; i++) await (phase === 'saving' ? inflight : run());
      return !pending && phase !== 'saving' && phase !== 'conflict';
    },
    /** The server was ahead of the version being signed: the text on screen is kept as unsaved and the editor's conflict handling takes over. */
    markStale(draft: NoteDraft) { pending = draft; clearTimeout(timer); set('conflict'); },
    version: () => base.version,
    unsaved: () => pending !== null || phase === 'saving',
    dispose: () => clearTimeout(timer),
  };
}
