import { useEffect, useRef, useState } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm, useWatch } from 'react-hook-form';
import { useI18n } from '@medvya/i18n';
import { ApiError } from '../../schedule/services/scheduleApi';
import { fetchReconciliation } from '../../reconciliation/services/reconciliationApi';
import { fetchProblems } from '../../problems/services/problemsApi';
import { remainingItems, sectionTicks, signChecklist } from '../domain/checklist';
import type { EncounterBanner } from '../domain/encounter';
import { autosaveDelayMs, saveRetryDelayMs, defaultTargetSection, insertAt, inTemplateOrder, phraseKey, vitalNameKey, type PreviousNote, type VerifiedVital, missingRequired, noteFormSchema, signFailure, switchChoices, switchTemplate, type NoteForm, type NoteSections, type NoteTemplate, type RecordingConsent, type SavePhase, type SignFailure } from '../domain/note';
import { fetchNote, fetchNoteAfterLeaving, fetchNoteCatalog, fetchPrefill, fetchPrevious, markLeftWithSave, saveNote, signNote, startReauth } from '../services/noteApi';
import { createAutosaver } from '../services/noteAutosaver';
import { forSave, unfinishedFindings } from '../domain/structured';

// A save still on its way when the doctor left the consultation: coming back reads the note only after it (MDV-P1-089).
const leaving = new Map<string, Promise<unknown>>();

/**
 * The consultation note editor (MDV-P2-009): template-driven sections, debounced autosave, template switch with a warning about text that has
 * no place in the new template, and conflict handling that keeps the doctor's text. The text lives in the form (memory) and on the server only.
 */
export function useNoteController(encounterId: string, canSign: boolean, timeZone: string | undefined, banner: EncounterBanner | null = null) {
  const { t, formatDate, formatTime } = useI18n();
  const queryClient = useQueryClient();
  // MDV-P1-089: each visit to the page reads the note again, after any save that leaving it started.
  const note = useQuery({ queryKey: ['note', encounterId], queryFn: async () => { await leaving.get(encounterId); return fetchNoteAfterLeaving(encounterId); },
    retry: false, staleTime: Infinity, refetchOnWindowFocus: false, refetchOnMount: 'always' });
  // Only what the server answered since this page opened: a copy cached by an earlier visit may be older than the server's.
  const held = note.isFetchedAfterMount && !note.isError ? note.data : undefined;
  const catalog = useQuery({ queryKey: ['note-templates'], queryFn: fetchNoteCatalog, retry: false });
  const form = useForm<NoteForm>({ resolver: zodResolver(noteFormSchema), defaultValues: { sections: {} } });
  const [phase, setPhase] = useState<SavePhase>('idle');
  const [templateKey, setTemplateKey] = useState<string | null>(null);
  const [pendingSwitch, setPendingSwitch] = useState<{ template: NoteTemplate; dropped: string[] } | null>(null);
  const [reloading, setReloading] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [signing, setSigning] = useState(false);
  const [signProblem, setSignProblem] = useState<SignFailure | { kind: 'unsaved' } | { kind: 'finding' } | null>(null);
  const keyRef = useRef<string | null>(null);
  // Verified prefill, quick phrases and earlier-note snippets (MDV-P2-009): read for a draft only, and only ever added by a button the doctor presses.
  const isDraft = !!held && held.status === 'Draft';
  const offer = { retry: false, staleTime: Infinity, refetchOnWindowFocus: false, enabled: isDraft } as const;
  const prefill = useQuery({ queryKey: ['note-prefill', encounterId], queryFn: () => fetchPrefill(encounterId), ...offer });
  const previous = useQuery({ queryKey: ['note-previous', encounterId], queryFn: () => fetchPrevious(encounterId), ...offer });
  const [target, setTarget] = useState(defaultTargetSection);
  const [tooLong, setTooLong] = useState(false);
  const cursor = useRef<{ id: string; start: number; end: number } | null>(null);
  const fields = useRef<Record<string, HTMLTextAreaElement | null>>({});
  const [savedAt, setSavedAt] = useState<Date | null>(null);
  // Every answer to a save goes into the query cache, also one that arrives after the doctor left the page.
  const [saver] = useState(() => createAutosaver({ save: (d, v, keepalive) => { if (keepalive) markLeftWithSave(encounterId, v); return saveNote(encounterId, d, v, keepalive); }, delayMs: autosaveDelayMs, retryMs: saveRetryDelayMs, onPhase: setPhase,
    onSaved: view => { setSavedAt(new Date()); queryClient.setQueryData(['note', encounterId], view); } }));
  // MDV-P2-044: the recording consent is part of the note and saved with it; the checklist reads it with the visit's reconciliation.
  const [consent, setConsent] = useState<RecordingConsent | null>(null);
  const reconciliation = useQuery({ queryKey: ['reconciliation', encounterId], queryFn: () => fetchReconciliation(encounterId), retry: false, enabled: isDraft && canSign });
  // The visit's problem list (MDV audit UX-11): "Diagnoses reviewed" is done only once a diagnosis is on it. Same key as the problems page.
  const problems = useQuery({ queryKey: ['problems', encounterId], queryFn: () => fetchProblems(encounterId), retry: false, enabled: isDraft && canSign });
  const templateOf = (key: string | null) => catalog.data?.templates.find(t => t.key === key);
  const draft = (sections: NoteSections, key = templateKey, recordingConsent = consent) => {
    const to = templateOf(key);
    return key ? { templateKey: key, sections: to ? forSave(to, sections) : sections, recordingConsent } : null;
  };
  const queue = (d: ReturnType<typeof draft>) => { if (d) saver.edit(d); };

  useEffect(() => {
    if (held && keyRef.current === null) {
      keyRef.current = held.templateKey;
      saver.setBase(held);
      form.reset({ sections: held.sections });
      setTemplateKey(held.templateKey);
      setConsent(held.recordingConsent ?? null);
    }
  }, [held, form, saver]);

  // Leaving the page saves what is typed, and so does a reload or a closed tab (pagehide, with keepalive); closing the tab warns while text is
  // unsaved; coming back online retries a failed save.
  useEffect(() => {
    // The unsent text goes out already at beforeunload, while the page is fully alive, not only at pagehide during teardown; sent twice it is not.
    const warn = (e: BeforeUnloadEvent) => { if (saver.unsaved()) { e.preventDefault(); saver.leave(); } };
    const online = () => void saver.retry();
    window.addEventListener('beforeunload', warn);
    window.addEventListener('pagehide', saver.leave);
    window.addEventListener('online', online);
    return () => {
      window.removeEventListener('beforeunload', warn); window.removeEventListener('pagehide', saver.leave); window.removeEventListener('online', online);
      leaving.set(encounterId, saver.flush().finally(saver.dispose));
    };
  }, [saver, encounterId]);

  const template = templateOf(templateKey) ?? null;
  const request = (key: string) => {
    const to = catalog.data?.templates.find(t => t.key === key);
    // Next tick: with Enter on the Select's option, the key's keypress lands on whatever has focus once the dialog opens, which would confirm the switch unseen.
    if (to && key !== templateKey) setTimeout(() => setPendingSwitch({ template: to, dropped: switchTemplate(form.getValues('sections'), to).dropped }), 0);
  };

  // Sign (MDV-P2-012): save what is typed, sign the version just saved, and on "reauth_required" send the doctor to confirm their identity.
  // The draft is on the server by then, so the redirect back reloads it and Sign is offered again.
  const sections = useWatch({ control: form.control, name: 'sections' });
  const checklist = isDraft && canSign ? signChecklist({
    identityShown: !!banner, allergyState: banner?.allergies.state ?? null,
    reconciliation: reconciliation.data ?? null, recordingConsent: consent, undecidedAi: held?.undecidedAi ?? [], sections,
    diagnosisRecorded: problems.data ? problems.data.rows.some(r => r.addedHere || r.status === 'Active') : null,
  }) : [];
  const remaining = remainingItems(checklist);
  const requestSign = () => {
    if (remaining > 0) return;
    // MDV-P2-042: an abnormal finding without its note is not saved yet; signing points to it instead of signing without it.
    const finding = template ? unfinishedFindings(template, form.getValues('sections'))[0] : undefined;
    if (finding) { setSignProblem({ kind: 'finding' }); document.getElementById(`note-${finding.replace('.', '-')}-note`)?.focus(); return; }
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
      else if (failure.kind === 'stale' && templateKey) saver.markStale({ templateKey, sections: form.getValues('sections'), recordingConsent: consent });
      else if (failure.kind === 'unreviewed') void queryClient.invalidateQueries({ queryKey: ['note', encounterId] }); // the checklist shows what the server holds
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
    queue(draft(form.getValues('sections')));
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
    vitals: prefill.data?.vitals.map((v, i) => ({ line: lines[i]!, flag: v.flag })) ?? [],
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
    form, assist, checklist, remaining,
    // MDV-P2-041: the one page's section bar; worked out from the same form the guided steps edit.
    ticks: template && held ? sectionTicks({ template, sections, undecidedAi: held.undecidedAi ?? [], vitals: prefill.data?.vitals.length ?? 0, signed: !isDraft }) : null,
    consent,
    onConsent: (value: RecordingConsent) => { setConsent(value); queue(draft(form.getValues('sections'), templateKey, value)); },
    canSign: canSign && !!held && held.status === 'Draft',
    signature: held?.signature ?? null,
    signAsName: held?.signAsName ?? null,
    confirming, signing, signProblem,
    onRequestSign: requestSign,
    onCancelSign: () => setConfirming(false),
    onConfirmSign: () => void confirmSign(),
    // MDV-P2-042: a structured section's answers, set by the chips and rows and saved like typing.
    answers: (id: string) => sections?.[id] ?? '',
    onAnswers: (id: string, text: string) => { form.setValue(`sections.${id}`, text); queue(draft(form.getValues('sections'))); },
    // Until the first save: from then on it is the doctor's note on the template they kept or chose.
    suggested: phase === 'idle' && !!held && held.version === 0 && !!held.suggestedTemplateKey && held.suggestedTemplateKey === templateKey,
    // Registered per section so only typing (not a programmatic reset) queues a save.
    register: (id: string) => {
      const field = form.register(`sections.${id}`, {
        onChange: () => queue(draft(form.getValues('sections'))),
        // Where the cursor was when the doctor left the field: the buttons that add text insert there.
        onBlur: e => { const el = e.target as HTMLTextAreaElement; cursor.current = { id, start: el.selectionStart, end: el.selectionEnd }; setTarget(id); },
      });
      return { ...field, ref: (el: HTMLTextAreaElement | null) => { field.ref(el); fields.current[id] = el; } };
    },
    loading: (!held && !note.isError) || catalog.isPending,
    forbidden: note.error instanceof ApiError && note.error.status === 403,
    loadFailed: note.isError || catalog.isError || (!!held && !!catalog.data && templateKey !== null && !template),
    template,
    templateKey,
    choices: catalog.data && templateKey ? switchChoices(catalog.data, templateKey) : [],
    readOnly: !!held && held.status !== 'Draft',
    phase,
    savedAt: savedAt && formatTime(savedAt, timeZone),
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
      queue(draft(sections, pendingSwitch.template.key));
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
