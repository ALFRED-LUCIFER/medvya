import { useEffect, useRef, useState } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm, useWatch } from 'react-hook-form';
import { ApiError } from '../../schedule/services/scheduleApi';
import { overrideSchema } from '../domain/checks';
import { startReauth } from '../../encounters/services/noteApi';
import { authoriseProblem, type AuthoriseProblem } from '../domain/authorisation';
import { withDefaults } from '../domain/addMedicine';
import { freeTextSchema, invalidFields, problemOf, toDraft, type PrintLanguage, type RxLine, type RxProblem, type SaveState, type SigDraft, type SigField } from '../domain/prescription';
import {
  addFreeText, addFromFormulary, authorise, cancelPrescription, fetchAuthorisation, fetchChecks, fetchPrescription, includeLine, overrideAlert, saveSig, searchMedicines,
} from '../services/prescriptionApi';
import { useDidYouMean } from './useAddMedicineController';

const autosaveMs = 700;

/**
 * The draft prescription of one consultation (MDV-P2-015). The server decides who may prescribe: a refusal (403) hides the panel. Every answer
 * is the whole draft (with the readable instruction in each print language), which replaces the cached one. The dose builder autosaves each
 * line a moment after the last change; a pending save is sent at once when the panel closes, so nothing typed is lost.
 * The safety checks (MDV-P2-016) run on the server again after every change; a severe alert takes a reason, one alert at a time.
 * Authorising (MDV-P2-017) is its own action: the server asks for the step-up (403 reauth_required) and runs the checks again before it agrees.
 */
export function usePrescriptionController(encounterId: string) {
  const queryClient = useQueryClient();
  const key = ['prescription', encounterId];
  const list = useQuery({ queryKey: key, queryFn: () => fetchPrescription(encounterId), retry: false });
  const checksKey = ['prescription-checks', encounterId];
  const checks = useQuery({ queryKey: checksKey, queryFn: () => fetchChecks(encounterId), enabled: list.isSuccess, retry: false });
  const reasonForm = useForm<{ reason: string }>({ resolver: zodResolver(overrideSchema), defaultValues: { reason: '' } });
  const [overriding, setOverriding] = useState<string | null>(null);
  const add = useForm<{ medicine: string }>({ resolver: zodResolver(freeTextSchema), defaultValues: { medicine: '' } });
  const text = (useWatch({ control: add.control, name: 'medicine' }) ?? '').trim();
  const search = useQuery({ queryKey: ['formulary', 'doctor', text], queryFn: () => searchMedicines(text), enabled: list.isSuccess && text.length >= 2, placeholderData: keepPreviousData });
  const suggestions = useDidYouMean(text, text.length >= 2 && search.isSuccess && (search.data ?? []).length === 0);
  const [drafts, setDrafts] = useState<Record<string, SigDraft>>({});
  const [saves, setSaves] = useState<Record<string, SaveState>>({});
  const [problem, setProblem] = useState<RxProblem | null>(null);
  const [language, setLanguage] = useState<PrintLanguage>('en');
  const pending = useRef(new Map<string, { timer: ReturnType<typeof setTimeout>; draft: SigDraft }>());

  // MDV-P2-017: the authorisation and whether the draft changed since; every draft change asks again.
  const authKey = ['prescription-authorisation', encounterId];
  const authorisation = useQuery({ queryKey: authKey, queryFn: () => fetchAuthorisation(encounterId), enabled: list.isSuccess, retry: false });
  const [authProblem, setAuthProblem] = useState<AuthoriseProblem | null>(null);
  const doAuthorise = useMutation({
    mutationFn: () => authorise(encounterId),
    onSuccess: s => { queryClient.setQueryData(authKey, s); setAuthProblem(null); },
    onError: e => { setAuthProblem(authoriseProblem(e instanceof ApiError ? e.status : undefined, e instanceof ApiError ? e.code : undefined)); recheck(); },
  });
  // Cancelling (MDV-P2-017): a reason, and the same step-up as authorising; the reason form reuses the override's rule (1 to 200 characters).
  const cancelForm = useForm<{ reason: string }>({ resolver: zodResolver(overrideSchema), defaultValues: { reason: '' } });
  const [cancelling, setCancelling] = useState(false);
  const [cancelProblem, setCancelProblem] = useState<'reauth' | 'failed' | null>(null);
  const doCancel = useMutation({
    mutationFn: (reason: string) => cancelPrescription(encounterId, reason),
    onSuccess: s => { queryClient.setQueryData(authKey, s); setCancelling(false); setCancelProblem(null); cancelForm.reset({ reason: '' }); },
    onError: e => setCancelProblem(authoriseProblem(e instanceof ApiError ? e.status : undefined, e instanceof ApiError ? e.code : undefined) === 'reauth' ? 'reauth' : 'failed'),
  });
  const recheck = () => {
    void queryClient.invalidateQueries({ queryKey: checksKey });
    void queryClient.invalidateQueries({ queryKey: authKey });
  };
  const take = (lines: RxLine[]) => { queryClient.setQueryData(key, lines); setProblem(null); recheck(); };
  const failed = (e: unknown) => setProblem(problemOf(e instanceof ApiError ? e.code : undefined));
  const mark = (id: string, state: SaveState) => setSaves(s => ({ ...s, [id]: state }));

  const addMedicine = useMutation({ mutationFn: (id: string) => addFromFormulary(encounterId, id), onSuccess: l => { take(l); add.reset({ medicine: '' }); }, onError: failed });
  const addText = useMutation({ mutationFn: (name: string) => addFreeText(encounterId, name), onSuccess: l => { take(l); add.reset({ medicine: '' }); }, onError: failed });
  const include = useMutation({ mutationFn: (v: { id: string; included: boolean }) => includeLine(v.id, v.included), onSuccess: take, onError: failed });
  const save = useMutation({
    mutationFn: (v: { id: string; draft: SigDraft }) => saveSig(v.id, v.draft),
    onSuccess: (l, v) => { queryClient.setQueryData(key, l); mark(v.id, 'saved'); recheck(); },
    onError: (e, v) => mark(v.id, problemOf(e instanceof ApiError ? e.code : undefined)),
  });

  const override = useMutation({
    mutationFn: (v: { key: string; reason: string }) => overrideAlert(encounterId, v.key, v.reason),
    onSuccess: c => { queryClient.setQueryData(checksKey, c); setOverriding(null); reasonForm.reset({ reason: '' }); setProblem(null); },
    onError: failed,
  });

  // Closing the consultation page sends what is still waiting, instead of dropping it.
  useEffect(() => () => {
    for (const [id, p] of pending.current) { clearTimeout(p.timer); void saveSig(id, p.draft).catch(() => undefined); }
    pending.current.clear();
  }, []);

  const lines = list.data ?? [];
  const draftOf = (l: RxLine) => (l.id && drafts[l.id]) || toDraft(l);
  const error = list.error instanceof ApiError ? list.error : null;
  return {
    lines,
    loading: list.isPending,
    /** Not this user's to see (the role, or no care relationship): the page shows nothing here. */
    hidden: error?.status === 403,
    failed: list.isError && error?.status !== 403,
    add,
    results: text.length >= 2 ? (search.data ?? []) : [],
    suggestions,
    searching: search.isFetching,
    language, onLanguage: setLanguage,
    problem,
    busy: addMedicine.isPending || addText.isPending || include.isPending || override.isPending,
    checks: checks.data ?? null,
    checksLoading: checks.isPending && list.isSuccess,
    checksFailed: checks.isError,
    overriding,
    reasonForm,
    authorisation: authorisation.data ?? null,
    authorising: doAuthorise.isPending,
    authProblem,
    onAuthorise: () => doAuthorise.mutate(),
    cancelling, cancelForm, cancelPending: doCancel.isPending, cancelProblem,
    onCancelling: (open: boolean) => { setCancelling(open); setCancelProblem(null); cancelForm.reset({ reason: '' }); },
    onCancel: cancelForm.handleSubmit(v => doCancel.mutate(v.reason)),
    /** The step-up leaves for the provider and comes back here; the draft is already saved on the server, nothing is put in browser storage. */
    onConfirmIdentity: async () => {
      try { await startReauth(); } catch { setAuthProblem('failed'); }
    },
    onOverride: (alertKey: string | null) => { setOverriding(alertKey); reasonForm.reset({ reason: '' }); },
    onReason: reasonForm.handleSubmit(v => { if (overriding) override.mutate({ key: overriding, reason: v.reason }); }),
    draftOf,
    saveOf: (id: string) => saves[id] ?? null,
    invalidOf: (l: RxLine) => invalidFields(draftOf(l)),
    onAddMedicine: (id: string) => addMedicine.mutate(id),
    onAddFreeText: add.handleSubmit(v => addText.mutate(v.medicine)),
    onInclude: (id: string, included: boolean) => include.mutate({ id, included }),
    onField: (line: RxLine, field: SigField, value: string) => {
      const id = line.id;
      if (!id) return;
      const before = draftOf(line);
      const draft = withDefaults(line, before, { ...before, [field]: value }, field);
      setDrafts(d => ({ ...d, [id]: draft }));
      const waiting = pending.current.get(id);
      if (waiting) clearTimeout(waiting.timer);
      pending.current.delete(id);
      if (invalidFields(draft).length > 0) return mark(id, 'invalid');
      mark(id, 'saving');
      pending.current.set(id, { draft, timer: setTimeout(() => { pending.current.delete(id); save.mutate({ id, draft }); }, autosaveMs) });
    },
  };
}
