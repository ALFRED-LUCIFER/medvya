import { useState } from 'react';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { ApiError } from '../../api/services/api';
import type { PlatformRole } from '../../session/domain/session';
import { reasonForm, type ReasonForm } from '../../team/domain/team';
import { canWrite, emptyForm, formOf, medicineForm, type Country, type Editing, type MedicineForm, type PendingAction, type RegulatoryClass } from '../domain/medicines';
import { addMedicine, correctMedicine, deactivate, searchMedicines, setClass } from '../services/medicinesApi';

const listKey = ['platform', 'medicines'] as const;

/** The Medicines screen (MDV-P5-064): search a country's shared list; owner and operations add, correct, set a class and deactivate, each with a reason. */
export function useMedicinesController(role: PlatformRole) {
  const client = useQueryClient();
  const [country, setCountry] = useState<Country>('IN');
  const [query, setQuery] = useState('');
  const [editing, setEditing] = useState<Editing | null>(null);
  const [pending, setPending] = useState<PendingAction | null>(null);
  const [classChoice, setClassChoice] = useState<RegulatoryClass>('NotVerified');
  const form = useForm<MedicineForm>({ resolver: zodResolver(medicineForm), defaultValues: emptyForm(), shouldFocusError: false });
  const reason = useForm<ReasonForm>({ resolver: zodResolver(reasonForm), defaultValues: { reason: '' }, shouldFocusError: false });

  const list = useQuery({ queryKey: [...listKey, country, query.trim()], queryFn: () => searchMedicines(country, query.trim()), placeholderData: keepPreviousData });
  const refresh = () => client.invalidateQueries({ queryKey: listKey });

  // A 400 names fields and codes: each goes under its field; a 409 (already listed) or 404 (gone) is said in the bar.
  const save = useMutation({
    mutationFn: (f: MedicineForm) => (editing?.kind === 'correct' ? correctMedicine(editing.medicine.id, f) : addMedicine(country, f)),
    onSuccess: () => { setEditing(null); form.reset(emptyForm()); void refresh(); },
    onError: e => {
      if (!(e instanceof ApiError) || e.status !== 400) return;
      for (const [field, codes] of Object.entries(e.fields))
        if (field in emptyForm()) form.setError(field as keyof MedicineForm, { message: codes[0] });
    },
  });
  const act = useMutation({
    mutationFn: ({ action, text }: { action: PendingAction; text: string }) =>
      action.kind === 'class' ? setClass(action.medicine.id, classChoice, text) : deactivate(action.medicine.id, text),
    onSuccess: () => { setPending(null); reason.reset(); void refresh(); },
  });

  const refusal = (e: unknown) =>
    !(e instanceof ApiError) ? 'failed' : e.status === 409 ? 'already_listed' : e.status === 404 ? 'gone' : e.fields.regClass?.[0] ?? (e.status === 400 ? null : 'failed');

  return {
    canWrite: canWrite(role),
    country, query,
    onCountry: (c: Country) => { setCountry(c); setEditing(null); setPending(null); },
    onQuery: (q: string) => setQuery(q),
    state: list.isPending ? 'loading' as const : list.isError ? 'error' as const : list.data.length === 0 ? 'empty' as const : 'ready' as const,
    medicines: list.data ?? [],
    editing, form, saving: save.isPending,
    saveProblem: save.isError ? refusal(save.error) : null,
    onStartAdd: () => { save.reset(); setPending(null); form.reset(emptyForm()); setEditing({ kind: 'add' }); },
    onStartCorrect: (m: PendingAction['medicine']) => { save.reset(); setPending(null); form.reset(formOf(m)); setEditing({ kind: 'correct', medicine: m }); },
    onCancelEdit: () => setEditing(null),
    onSave: form.handleSubmit(f => save.mutate(f)),
    pending, reason, classChoice, onClassChoice: setClassChoice, acting: act.isPending,
    actProblem: act.isError ? refusal(act.error) ?? 'failed' : null,
    onStartAction: (action: PendingAction) => {
      act.reset(); reason.reset(); setEditing(null); setPending(action);
      // Starts from the current class, so confirming without a choice changes nothing by accident.
      setClassChoice(action.medicine.regClass);
    },
    onCancelAction: () => setPending(null),
    onConfirmAction: reason.handleSubmit(({ reason: text }) => { if (pending) act.mutate({ action: pending, text }); }),
  };
}
