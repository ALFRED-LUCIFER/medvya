import { useEffect, useRef, useState } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { useI18n } from '@medvya/i18n';
import { ApiError } from '../../schedule/services/scheduleApi';
import { autosaveDelayMs, defaultTargetSection, insertAt, inTemplateOrder, phraseKey, vitalNameKey, type PreviousNote, type VerifiedVital, missingRequired, noteFormSchema, signFailure, switchChoices, switchTemplate, type NoteForm, type NoteTemplate, type SavePhase, type SignFailure } from '../domain/note';
import { fetchNote, fetchNoteCatalog, fetchPrefill, fetchPrevious, saveNote, signNote, startReauth } from '../services/noteApi';
import { createAutosaver } from '../services/noteAutosaver';

/**
 * The consultation note editor (MDV-P2-009): template-driven sections, debounced autosave, template switch with a warning about text that has
 * no place in the new template, and conflict handling that keeps the doctor's text. The text lives in the form (memory) and on the server only.
 */
export function useNoteController(encounterId: string, canSign: boolean, timeZone: string | undefined) {
  const { t, formatDate, formatTime } = useI18n();
  const queryClient = useQueryClient();
  const note = useQuery({ queryKey: ['note', encounterId], queryFn: () => fetchNote(encounterId), retry: false, staleTime: Infinity, refetchOnWindowFocus: false });
  const catalog = useQuery({ queryKey: ['note-templates'], queryFn: fetchNoteCatalog, retry: false });
  const form = useForm<NoteForm>({ resolver: zodResolver(noteFormSchema), defaultValues: { sections: {} } });
  const [phase, setPhase] = useState<SavePhase>('idle');
  const [templateKey, setTemplateKey] = useState<string | null>(null);
  const [pendingSwitch, setPendingSwitch] = useState<{ template: NoteTemplate; dropped: string[] } | null>(null);
  const [reloading, setReloading] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [signing, setSigning] = useState(false);
  const [signProblem, setSignProblem] = useState<SignFailure | { kind: 'unsaved' } | null>(null);
  const keyRef = useRef<string | null>(null);
  // Verified prefill, quick phrases and earlier-note snippets (MDV-P2-009): read for a draft only, and only ever added by a button the doctor presses.
  const isDraft = !!note.data && note.data.status === 'Draft';
  const offer = { retry: false, staleTime: Infinity, refetchOnWindowFocus: false, enabled: isDraft } as const;
  const prefill = useQuery({ queryKey: ['note-prefill', encounterId], queryFn: () => fetchPrefill(encounterId), ...offer });
  const previous = useQuery({ queryKey: ['note-previous', encounterId], queryFn: () => fetchPrevious(encounterId), ...offer });
  const [target, setTarget] = useState(defaultTargetSection);
  const [tooLong, setTooLong] = useState(false);
  const cursor = useRef<{ id: string; start: number; end: number } | null>(null);
  const fields = useRef<Record<string, HTMLTextAreaElement | null>>({});
  const [saver] = useState(() => createAutosaver({ save: (d, v) => saveNote(encounterId, d, v), delayMs: autosaveDelayMs, onPhase: setPhase }));

  useEffect(() => {
    if (note.data && keyRef.current === null) {
      keyRef.current = note.data.templateKey;
      saver.setBase(note.data);
      form.reset({ sections: note.data.sections });
      setTemplateKey(note.data.templateKey);
    }
  }, [note.data, form, saver]);

  // Leaving the page saves what is typed; closing the tab warns while text is unsaved; coming back online retries a failed save.
  useEffect(() => {
    const warn = (e: BeforeUnloadEvent) => { if (saver.unsaved()) e.preventDefault(); };
    const online = () => void saver.retry();
    window.addEventListener('beforeunload', warn);
    window.addEventListener('online', online);
    return () => { window.removeEventListener('beforeunload', warn); window.removeEventListener('online', online); void saver.retry(); };
  }, [saver]);

  const template = catalog.data?.templates.find(t => t.key === templateKey) ?? null;
  const request = (key: string) => {
    const to = catalog.data?.templates.find(t => t.key === key);
    // Next tick: with Enter on the Select's option, the key's keypress lands on whatever has focus once the dialog opens, which would confirm the switch unseen.
    if (to && key !== templateKey) setTimeout(() => setPendingSwitch({ template: to, dropped: switchTemplate(form.getValues('sections'), to).dropped }), 0);
  };

  // Sign (MDV-P2-012): save what is typed, sign the version just saved, and on "reauth_required" send the doctor to confirm their identity.
  // The draft is on the server by then, so the redirect back reloads it and Sign is offered again.
  const requestSign = () => {
    const blank = template ? inTemplateOrder(template, missingRequired(template, form.getValues('sections'))) : [];
    setSignProblem(blank.length ? { kind: 'blank', sections: blank } : null);
    if (blank.length) form.setFocus(`sections.${blank[0]!}`); else setConfirming(true);
  };
  const confirmSign = async () => {
    setSigning(true);
    try {
      if (!(await saver.flush())) { setSignProblem(phase === 'conflict' ? null : { kind: 'unsaved' }); return; }
      const view = await signNote(encounterId, saver.version());
      setSignProblem(null);
      queryClient.setQueryData(['note', encounterId], view);
    } catch (e) {
      const failure = e instanceof ApiError ? signFailure(e.status, e.body) : { kind: 'failed' as const };
      if (failure.kind === 'signed') void queryClient.invalidateQueries({ queryKey: ['note', encounterId] });
      else if (failure.kind === 'stale' && templateKey) saver.markStale({ templateKey, sections: form.getValues('sections') });
      else if (failure.kind === 'blank' && template) { failure.sections = inTemplateOrder(template, failure.sections); form.setFocus(`sections.${failure.sections[0]!}`); }
      setSignProblem(failure.kind === 'signed' || failure.kind === 'stale' ? null : failure);
      if (failure.kind === 'reauth') {
        try { await startReauth(); } catch { setSignProblem({ kind: 'failed' }); }
      }
    } finally {
      setConfirming(false);
      setSigning(false);
    }
  };

  const when = (iso: string) => ({ date: formatDate(new Date(iso), timeZone), time: formatTime(new Date(iso), timeZone) });
  const vitalLine = (v: VerifiedVital) => t('noteAssist.vitalLine', {
    label: t(vitalNameKey(v.code)), value: String(v.value), unit: v.unit,
    source: v.recordedByName ? t('noteAssist.source', { name: v.recordedByName, ...when(v.takenAt) }) : t('noteAssist.sourceNoName', when(v.takenAt)),
  });
  // Adds text to a section at the cursor (when that section was the last one edited) or at its end, saves it like typing, and puts the cursor after it.
  const insert = (id: string, snippet: string) => {
    if (!templateKey) return;
    const current = form.getValues(`sections.${id}`) ?? '';
    const at = cursor.current?.id === id ? cursor.current : { start: current.length, end: current.length };
    const next = insertAt(current, at.start, at.end, snippet);
    setTooLong(next === null);
    if (!next) return;
    form.setValue(`sections.${id}`, next.text);
    saver.edit({ templateKey, sections: form.getValues('sections') });
    cursor.current = { id, start: next.caret, end: next.caret };
    setTarget(id);
    const el = fields.current[id];
    el?.focus();
    el?.setSelectionRange(next.caret, next.caret);
  };
  const lines = prefill.data?.vitals.map(vitalLine) ?? [];
  const assist = !isDraft ? null : {
    phrases: template?.quickPhrases ?? [], target, tooLong,
    loading: prefill.isPending || previous.isPending, failed: prefill.isError || previous.isError,
    vitals: lines,
    previous: (previous.data?.notes ?? []).map((n: PreviousNote) => ({ id: n.encounterId, signedAt: n.signedAt, signedByName: n.signedByName, sections: n.sections })),
    onInsertPhrase: (key: string) => insert(target, t(phraseKey(key))),
    onInsertVitals: () => insert('objective', lines.join('\n')),
    onInsertSnippet: (noteId: string, sectionId: string) => {
      const n = previous.data?.notes.find(x => x.encounterId === noteId);
      const text = n?.sections[sectionId];
      if (n && text) insert(target, t('noteAssist.previous.copied', { text, date: formatDate(new Date(n.signedAt), timeZone) }));
    },
  };

  return {
    form, assist,
    canSign: canSign && !!note.data && note.data.status === 'Draft',
    signature: note.data?.signature ?? null,
    signAsName: note.data?.signAsName ?? null,
    confirming, signing, signProblem,
    onRequestSign: requestSign,
    onCancelSign: () => setConfirming(false),
    onConfirmSign: () => void confirmSign(),
    // Registered per section so only typing (not a programmatic reset) queues a save.
    register: (id: string) => {
      const field = form.register(`sections.${id}`, {
        onChange: () => { if (templateKey) saver.edit({ templateKey, sections: form.getValues('sections') }); },
        // Where the cursor was when the doctor left the field: the buttons that add text insert there.
        onBlur: e => { const el = e.target as HTMLTextAreaElement; cursor.current = { id, start: el.selectionStart, end: el.selectionEnd }; setTarget(id); },
      });
      return { ...field, ref: (el: HTMLTextAreaElement | null) => { field.ref(el); fields.current[id] = el; } };
    },
    loading: note.isPending || catalog.isPending,
    forbidden: note.error instanceof ApiError && note.error.status === 403,
    loadFailed: note.isError || catalog.isError || (!!note.data && !!catalog.data && templateKey !== null && !template),
    template,
    templateKey,
    choices: catalog.data && templateKey ? switchChoices(catalog.data, templateKey) : [],
    readOnly: !!note.data && note.data.status !== 'Draft',
    phase,
    reloading,
    pendingSwitch,
    onRequestSwitch: request,
    onCancelSwitch: () => setPendingSwitch(null),
    onConfirmSwitch: () => {
      if (!pendingSwitch) return;
      const sections = switchTemplate(form.getValues('sections'), pendingSwitch.template).kept;
      keyRef.current = pendingSwitch.template.key;
      setTemplateKey(pendingSwitch.template.key);
      form.reset({ sections });
      saver.edit({ templateKey: pendingSwitch.template.key, sections });
      setPendingSwitch(null);
    },
    onRetry: () => void saver.retry(),
    // Reload the newer version and lay the doctor's unsaved changes over it; nothing the doctor typed is dropped.
    onReapply: async () => {
      setReloading(true);
      try {
        const merged = saver.resolve(await fetchNote(encounterId));
        if (merged) form.reset({ sections: merged.sections });
      } catch { /* still in conflict: the doctor can try again */ } finally { setReloading(false); }
    },
  };
}
