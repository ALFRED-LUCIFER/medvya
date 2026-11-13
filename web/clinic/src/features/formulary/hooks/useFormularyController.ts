import { useDeferredValue, useState } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { useI18n } from '@medvya/i18n';
import { ApiError } from '../../schedule/services/scheduleApi';
import { useTableController } from '../../table/hooks/useTableController';
import {
  brandProblems, brandRowsSchema, emptyMedicine, formularySort, medicineSchema, orderSetSchema, parseBrandRows, splitProblem,
  type BrandProblem, type BrandRowsForm, type MedicineForm, type OrderSetForm, type ProblemCode,
} from '../domain/formulary';
import { addBrands, addMedicine, addOrderSet, fetchOrderSets, searchFormulary } from '../services/formularyApi';

const problemOf = (e: unknown) => splitProblem(e instanceof ApiError ? e.code : undefined);

/** Medicines tab (F-13): search the country list, add a clinic medicine. The server validates by the country's rules and answers with codes. */
export function useFormularyController() {
  const { t } = useI18n();
  const queryClient = useQueryClient();
  const [query, setQuery] = useState('');
  const deferred = useDeferredValue(query.trim());
  const list = useQuery({ queryKey: ['formulary', deferred], queryFn: () => searchFormulary(deferred), placeholderData: keepPreviousData });
  const items = list.data ?? [];
  const table = useTableController(items, formularySort, c => t(`formulary.col.${c}`));
  const form = useForm<MedicineForm>({ resolver: zodResolver(medicineSchema), defaultValues: emptyMedicine() });
  const [saved, setSaved] = useState(false);
  const [formError, setFormError] = useState<ProblemCode | null>(null);

  const add = useMutation({
    mutationFn: addMedicine,
    onSuccess: () => {
      form.reset(emptyMedicine());
      setSaved(true);
      setFormError(null);
      void queryClient.invalidateQueries({ queryKey: ['formulary'] });
    },
    onError: e => {
      setSaved(false);
      const { field, code } = problemOf(e);
      if (field && field in medicineSchema.shape) form.setError(field as keyof MedicineForm, { type: 'server', message: code });
      else setFormError(code);
    },
  });

  return {
    query,
    onQuery: setQuery,
    items,
    table,
    loading: list.isPending,
    failed: list.isError,
    form,
    saving: add.isPending,
    saved,
    formError,
    onSubmit: form.handleSubmit(values => { setSaved(false); add.mutate(values); }),
  };
}

/** Brand names in bulk (MDV-P1-088): the pasted rows go to the server, which adds all of them or none and names each row it refuses. */
export function useBrandsController() {
  const queryClient = useQueryClient();
  const form = useForm<BrandRowsForm>({ resolver: zodResolver(brandRowsSchema), defaultValues: { rows: '' } });
  const [added, setAdded] = useState<number | null>(null);
  const [problems, setProblems] = useState<BrandProblem[]>([]);

  const add = useMutation({
    mutationFn: addBrands,
    onSuccess: r => {
      form.reset();
      setAdded(r.added);
      setProblems([]);
      void queryClient.invalidateQueries({ queryKey: ['formulary'] });
    },
    onError: e => setProblems(brandProblems(e instanceof ApiError ? e.body : null)),
  });

  return {
    form,
    saving: add.isPending,
    added,
    problems,
    onSubmit: form.handleSubmit(values => {
      const parsed = parseBrandRows(values.rows);
      setAdded(null);
      setProblems(parsed.problems);
      if (parsed.problems.length === 0) add.mutate(parsed.rows);
    }),
  };
}

/** Order sets: the clinic's list and the form to add one. */
export function useOrderSetsController() {
  const queryClient = useQueryClient();
  const list = useQuery({ queryKey: ['order-sets'], queryFn: fetchOrderSets });
  const form = useForm<OrderSetForm>({ resolver: zodResolver(orderSetSchema), defaultValues: { name: '', items: '' } });
  const [saved, setSaved] = useState(false);
  const [formError, setFormError] = useState<ProblemCode | null>(null);

  const add = useMutation({
    mutationFn: addOrderSet,
    onSuccess: () => {
      form.reset();
      setSaved(true);
      setFormError(null);
      void queryClient.invalidateQueries({ queryKey: ['order-sets'] });
    },
    onError: e => {
      setSaved(false);
      const { field, code } = problemOf(e);
      if (field === 'name' || field === 'items') form.setError(field, { type: 'server', message: code });
      else setFormError(code);
    },
  });

  return {
    sets: list.data ?? [],
    loading: list.isPending,
    failed: list.isError,
    form,
    saving: add.isPending,
    saved,
    formError,
    onSubmit: form.handleSubmit(values => { setSaved(false); add.mutate(values); }),
  };
}
