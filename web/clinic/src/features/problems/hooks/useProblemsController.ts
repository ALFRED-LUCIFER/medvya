import { useMemo, useState } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { refusalOf } from '../../clinical-access/domain/clinicalAccess';
import { fetchEncounter } from '../../encounters/services/encountersApi';
import { ApiError } from '../../schedule/services/scheduleApi';
import { addSchema, editSchema, emptyAdd, problemOf, searchCodes, type AddDraft, type DiagnosisCode, type EditDraft, type Problem, type ProblemList, type ProblemRow } from '../domain/problems';
import { addProblem, fetchCodeSet, fetchProblems, updateProblem } from '../services/problemsApi';

/**
 * One consultation's problem list (E-07). The server answers every write with the whole list, which replaces the cached one. ICD-10 search runs
 * over the country's code set in the browser; a code is optional, and without one the doctor's own description is what is recorded.
 */
export function useProblemsController(encounterId: string) {
  const queryClient = useQueryClient();
  const key = ['problems', encounterId];
  const query = useQuery({ queryKey: key, queryFn: () => fetchProblems(encounterId), retry: false });
  const codeSet = useQuery({ queryKey: ['diagnosis-codes'], queryFn: fetchCodeSet, staleTime: Infinity, retry: false });
  const banner = useQuery({ queryKey: ['encounter', encounterId], queryFn: () => fetchEncounter(encounterId), retry: false });
  const addForm = useForm<AddDraft>({ resolver: zodResolver(addSchema), defaultValues: emptyAdd });
  const editForm = useForm<EditDraft>({ resolver: zodResolver(editSchema) });
  const [search, setSearch] = useState('');
  const [picked, setPicked] = useState<DiagnosisCode | null>(null);
  const [editing, setEditing] = useState<string | null>(null);
  const [problem, setProblem] = useState<Problem | null>(null);
  const results = useMemo(() => (picked ? [] : searchCodes(codeSet.data?.codes ?? [], search)), [codeSet.data, search, picked]);

  const fail = (e: unknown) => setProblem(problemOf(e instanceof ApiError ? e.code : undefined));
  const add = useMutation({
    mutationFn: (d: AddDraft) => addProblem(encounterId, { code: picked?.code ?? null, description: d.description || null, verification: d.verification, onsetOn: d.onsetOn || null }),
    onSuccess: (view: ProblemList) => {
      setProblem(null);
      setPicked(null);
      setSearch('');
      addForm.reset(emptyAdd);
      queryClient.setQueryData(key, view);
    },
    onError: fail,
  });
  const update = useMutation({
    mutationFn: ({ id, d }: { id: string; d: EditDraft }) => updateProblem(encounterId, id, { verification: d.verification, status: d.status, onsetOn: d.onsetOn || null }),
    onSuccess: (view: ProblemList) => {
      setProblem(null);
      setEditing(null);
      queryClient.setQueryData(key, view);
    },
    onError: fail,
  });

  const error = query.error instanceof ApiError ? query.error : null;
  return {
    view: query.data ?? null,
    banner: banner.data ?? null,
    codeSet: codeSet.data ?? null,
    loading: query.isPending,
    notFound: error?.status === 404,
    refusal: error?.status === 403 ? (refusalOf(error.body) ?? { canRequest: false, requestPending: false }) : null,
    failed: query.isError && error?.status !== 404 && error?.status !== 403,
    addForm,
    editForm,
    search,
    results,
    picked,
    editing,
    problem,
    adding: add.isPending,
    saving: update.isPending,
    onSearch: setSearch,
    onPick: (c: DiagnosisCode) => setPicked(c),
    onClearPick: () => setPicked(null),
    // Neither a code nor words: nothing to record. Said here first; the server refuses it too.
    onAdd: addForm.handleSubmit(d => {
      if (!picked && d.description === '') addForm.setError('description', { type: 'required' });
      else add.mutate(d);
    }),
    onEdit: (r: ProblemRow) => {
      setProblem(null);
      editForm.reset({ verification: r.verification, status: r.status, onsetOn: r.onsetOn ?? '' });
      setEditing(r.id);
    },
    onCancelEdit: () => setEditing(null),
    onSaveEdit: editForm.handleSubmit(d => editing && update.mutate({ id: editing, d })),
  };
}
