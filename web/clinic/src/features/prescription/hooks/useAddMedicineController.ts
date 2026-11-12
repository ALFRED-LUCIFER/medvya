import { useEffect, useRef, useState } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm, useWatch } from 'react-hook-form';
import { ApiError } from '../../schedule/services/scheduleApi';
import { checksFor, draftOf, medicinesOf, quantityOf, sameMedicine, unitOf, type Choice } from '../domain/addMedicine';
import { lineSafety, overrideSchema } from '../domain/checks';
import { invalidFields, problemOf, type Medicine, type RxLine, type RxProblem } from '../domain/prescription';
import { addFromFormulary, fetchChecks, fetchPrescription, includeLine, overrideAlert, saveSig, searchMedicines } from '../services/prescriptionApi';

const autosaveMs = 500;
const none: Choice = { often: null, when: null, long: null, instructions: '' };

/**
 * The Add medicine panel (MDV-P2-039) over the consultation's draft prescription (MDV-P2-015). Picking a strength puts the line on the draft,
 * so the server's own checks (MDV-P2-016, MDV-P2-040) and its override path cover it; the choices autosave onto the existing sig. "Add" keeps
 * the line, and a severe finding without a reason blocks it. Closing without adding, or choosing another medicine, switches the line off
 * (nothing clinical is deleted). With no open consultation (encounterId null) nothing can be added.
 * shortcut: a line switched off here stays on the draft as "not included"; a delete for never-added lines when doctors find the composer noisy.
 */
export function useAddMedicineController(encounterId: string | null) {
  const queryClient = useQueryClient();
  const key = ['prescription', encounterId];
  const checksKey = ['prescription-checks', encounterId];
  const [open, setOpen] = useState(false);
  const [medicine, setMedicine] = useState<Medicine | null>(null);
  const [lineId, setLineId] = useState<string | null>(null);
  const [choice, setChoice] = useState<Choice>(none);
  const [dirty, setDirty] = useState(false);
  const [problem, setProblem] = useState<RxProblem | null>(null);
  const [added, setAdded] = useState<string | null>(null);
  const [overriding, setOverriding] = useState<string | null>(null);
  const reasonForm = useForm<{ reason: string }>({ resolver: zodResolver(overrideSchema), defaultValues: { reason: '' } });
  const search = useForm<{ medicine: string }>({ defaultValues: { medicine: '' } });
  const text = (useWatch({ control: search.control, name: 'medicine' }) ?? '').trim();
  const found = useQuery({
    queryKey: ['formulary', 'doctor', text], queryFn: () => searchMedicines(text), enabled: open && !!encounterId && text.length >= 2, placeholderData: keepPreviousData,
  });
  const list = useQuery({ queryKey: key, queryFn: () => fetchPrescription(encounterId!), enabled: open && !!encounterId, retry: false });
  const checks = useQuery({ queryKey: checksKey, queryFn: () => fetchChecks(encounterId!), enabled: !!lineId, retry: false });

  const failed = (e: unknown) => setProblem(problemOf(e instanceof ApiError ? e.code : undefined));
  const take = (lines: RxLine[]) => {
    queryClient.setQueryData(key, lines);
    void queryClient.invalidateQueries({ queryKey: checksKey });
    void queryClient.invalidateQueries({ queryKey: ['prescription-authorisation', encounterId] });
  };
  // The line not yet added is switched off when the panel closes or the page goes, so nothing is prescribed by leaving.
  const staged = useRef<string | null>(null);
  // The latest choices (set where they change) and the pending autosave, so a late answer never marks newer choices as saved.
  const latest = useRef<Choice>(none);
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const switchOff = (id: string) => includeLine(id, false).then(take, () => undefined);
  useEffect(() => () => { clearTimeout(timer.current); if (staged.current) void includeLine(staged.current, false).catch(() => undefined); }, []);

  const stage = useMutation({
    mutationFn: async (m: Medicine) => {
      if (staged.current) await includeLine(staged.current, false);
      const before = new Set((queryClient.getQueryData<RxLine[]>(key) ?? []).map(l => l.id));
      const lines = await addFromFormulary(encounterId!, m.id);
      return { lines, id: lines.find(l => l.kind === 'Prescribe' && l.id && !before.has(l.id))?.id ?? null };
    },
    onSuccess: ({ lines, id }, m) => { take(lines); staged.current = id; setLineId(id); setProblem(null); if (id) queue(id, m, latest.current); },
    onError: failed,
  });
  const save = useMutation({
    mutationFn: (v: { id: string; choice: Choice; medicine: Medicine }) => saveSig(v.id, draftOf(v.medicine, v.choice)),
    onSuccess: (lines, v) => { take(lines); if (v.choice === latest.current) setDirty(false); },
    onError: failed,
  });
  // Each change autosaves a moment after the last one, as in the composer; an invalid draft waits for the fix.
  const queue = (id: string, m: Medicine, c: Choice) => {
    clearTimeout(timer.current);
    setDirty(true);
    if (invalidFields(draftOf(m, c)).length === 0) timer.current = setTimeout(() => save.mutate({ id, choice: c, medicine: m }), autosaveMs);
  };
  const draft = medicine && draftOf(medicine, choice);
  const invalid = draft ? invalidFields(draft) : [];

  const override = useMutation({
    mutationFn: (v: { key: string; reason: string }) => overrideAlert(encounterId!, v.key, v.reason),
    onSuccess: c => { queryClient.setQueryData(checksKey, c); setOverriding(null); reasonForm.reset({ reason: '' }); },
    onError: failed,
  });

  const reset = () => { clearTimeout(timer.current); staged.current = null; latest.current = none; setMedicine(null); setLineId(null); setChoice(none); setDirty(false); setProblem(null); setOverriding(null); search.reset({ medicine: '' }); };
  const close = () => { if (staged.current) void switchOff(staged.current); reset(); setOpen(false); };
  const line = list.data?.find(l => l.id === lineId) ?? null;
  const results = text.length >= 2 ? (found.data ?? []) : [];
  const mine = checks.data && lineId ? checksFor(checks.data, lineId) : null;
  const blocked = !!mine && !!lineId && lineSafety(mine, lineId) === 'severe';
  const chosen = !!choice.often && !!choice.when && !!choice.long;
  const busy = stage.isPending || override.isPending;
  const settled = !!line && !dirty && !save.isPending && !checks.isFetching && !!mine;

  return {
    open, encounterId,
    /** Not this user's to prescribe (the role, or no care relationship): the server says so and the panel says nothing can be added. */
    refused: list.error instanceof ApiError && list.error.status === 403,
    onOpen: () => { setAdded(null); setOpen(true); },
    onClose: close,
    search, results: medicinesOf(results), searching: found.isFetching, noResults: text.length >= 2 && found.isSuccess && results.length === 0,
    medicine, strengths: medicine ? results.filter(m => sameMedicine(m, medicine)) : [],
    onMedicine: (m: Medicine) => { setMedicine(m); stage.mutate(m); },
    choice,
    onChoice: (c: Partial<Choice>) => {
      const next = { ...choice, ...c };
      latest.current = next;
      setChoice(next);
      if (lineId && medicine) queue(lineId, medicine, next);
    },
    instructionsInvalid: invalid.includes('instructions'),
    line, quantity: medicine && choice.often && choice.long ? quantityOf(choice.often, choice.long, unitOf(medicine.form)) : null,
    saving: dirty || save.isPending,
    problem, busy, blocked, chosen,
    canAdd: chosen && settled && !blocked && !busy && invalid.length === 0,
    onAdd: () => { setAdded(line?.medicine ?? null); reset(); setOpen(false); },
    added,
    // The existing safety checks, narrowed to this line: new alert kinds show without a change here.
    checks: mine, checksLoading: !!lineId && checks.isPending, checksFailed: checks.isError, ended: false,
    overriding, reasonForm,
    onOverride: (alertKey: string | null) => { setOverriding(alertKey); reasonForm.reset({ reason: '' }); },
    onReason: reasonForm.handleSubmit(v => { if (overriding) override.mutate({ key: overriding, reason: v.reason }); }),
  };
}
