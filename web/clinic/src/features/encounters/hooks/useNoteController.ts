import { useEffect, useRef, useState } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { ApiError } from '../../schedule/services/scheduleApi';
import { autosaveDelayMs, inTemplateOrder, missingRequired, noteFormSchema, signFailure, switchChoices, switchTemplate, type NoteForm, type NoteTemplate, type SavePhase, type SignFailure } from '../domain/note';
import { fetchNote, fetchNoteCatalog, saveNote, signNote, startReauth } from '../services/noteApi';
import { createAutosaver } from '../services/noteAutosaver';

/**
 * The consultation note editor (MDV-P2-009): template-driven sections, debounced autosave, template switch with a warning about text that has
 * no place in the new template, and conflict handling that keeps the doctor's text. The text lives in the form (memory) and on the server only.
 */
export function useNoteController(encounterId: string, canSign: boolean) {
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
    if (to && key !== templateKey) setPendingSwitch({ template: to, dropped: switchTemplate(form.getValues('sections'), to).dropped });
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

  return {
    form,
    canSign: canSign && !!note.data && note.data.status === 'Draft',
    signature: note.data?.signature ?? null,
    confirming, signing, signProblem,
    onRequestSign: requestSign,
    onCancelSign: () => setConfirming(false),
    onConfirmSign: () => void confirmSign(),
    // Registered per section so only typing (not a programmatic reset) queues a save.
    register: (id: string) => form.register(`sections.${id}`, {
      onChange: () => { if (templateKey) saver.edit({ templateKey, sections: form.getValues('sections') }); },
    }),
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
