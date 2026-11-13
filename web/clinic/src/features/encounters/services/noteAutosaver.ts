import { ApiError } from '../../schedule/services/scheduleApi';
import { reapply, staleVersion, type NoteDraft, type NoteSections, type NoteView, type SavePhase } from '../domain/note';

type Options = {
  /** `keepalive`: the page is going away, and the browser must finish the request after it. */
  save: (draft: NoteDraft, baseVersion: number, keepalive: boolean) => Promise<NoteView>;
  delayMs: number; retryMs: number; onPhase: (phase: SavePhase) => void;
  /** What the server answered to a save: the only moment the note counts as saved. */
  onSaved?: (view: NoteView) => void;
};

/**
 * Debounced autosave of one note, in memory only (no PHI in browser storage). One save at a time: text typed while one is in flight goes in the
 * next. A failed save keeps the text and tries again every `retryMs`, on the next edit or on `retry()`. A stale save (409) holds every later save until `resolve()`,
 * so the doctor's text is never overwritten on the server and never lost here.
 */
export function createAutosaver({ save, delayMs, retryMs, onPhase, onSaved }: Options) {
  let base = { version: 0, sections: {} as NoteSections };
  let pending: NoteDraft | null = null;
  let phase: SavePhase = 'idle';
  let timer: ReturnType<typeof setTimeout> | undefined;
  let inflight: Promise<void> = Promise.resolve();
  const set = (p: SavePhase) => { phase = p; onPhase(p); };

  function run(keepalive = false) {
    clearTimeout(timer);
    if ((phase === 'saving' && !keepalive) || phase === 'conflict' || !pending) return Promise.resolve();
    const draft = pending;
    pending = null;
    set('saving');
    return (inflight = attempt(draft, keepalive));
  }

  async function attempt(draft: NoteDraft, keepalive: boolean) {
    try {
      const view = await save(draft, base.version, keepalive);
      base = { version: view.version, sections: view.sections };
      onSaved?.(view);
      if (pending) void run(); else set('saved');
    } catch (e) {
      pending ??= draft;
      const stale = e instanceof ApiError && staleVersion(e.status, e.body) !== null;
      set(stale ? 'conflict' : 'failed');
      // "Not saved. Trying again." must be true (MDV-P1-089). shortcut: a fixed delay and no limit, also for a refusal that will not change (403); back off or stop when a clinic's logs show it matters.
      if (!stale) timer = setTimeout(() => void run(), retryMs);
    }
  }

  return {
    /** The note as the server last had it (on load). */
    setBase(view: NoteView) { base = { version: view.version, sections: view.sections }; },
    edit(draft: NoteDraft) { pending = draft; clearTimeout(timer); if (phase !== 'conflict') timer = setTimeout(() => void run(), delayMs); },
    retry: () => run(),
    /**
     * The page is going away (pagehide): sends the unsent text now, with keepalive, even while an earlier save is in flight.
     * shortcut: the browser refuses a keepalive body over 64 KiB, and a save in flight that lands first makes this one stale; both lose only the text
     * typed in the last autosave delay. Upgrade with a server endpoint that takes the text without a version if a clinic reports it.
     */
    leave: () => void run(true),
    /** Lays the unsaved text over the server's newer version, saves that, and returns it for the editor to show. */
    resolve(theirs: NoteView): NoteDraft | null {
      if (!pending) return null;
      const merged = { ...pending, sections: reapply(base.sections, pending.sections, theirs.sections) };
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
