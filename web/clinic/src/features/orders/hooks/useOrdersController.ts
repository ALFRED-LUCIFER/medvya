import { useState } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { ApiError } from '../../schedule/services/scheduleApi';
import { emptyOrder, orderSchema, problemOf, type Order, type OrderForm, type OrderProblem } from '../domain/orders';
import { usePaperPdf } from '../../prescription/hooks/useRxPrintController';
import { addOrder, addOrderSet, downloadSlipPdf, fetchOrders, fetchOrderSets, fetchSlip, includeOrder, placeOrders, printPage } from '../services/ordersApi';

/**
 * The orders of one consultation (MDV-P2-018). The server decides who may order: a refusal (403) hides the panel. Every answer is the
 * consultation's full list, so the screen never works out a state itself.
 */
export function useOrdersController(encounterId: string) {
  const queryClient = useQueryClient();
  const key = ['orders', encounterId];
  const list = useQuery({ queryKey: key, queryFn: () => fetchOrders(encounterId), retry: false });
  const sets = useQuery({ queryKey: ['order-sets', 'doctor'], queryFn: fetchOrderSets, enabled: list.isSuccess });
  const form = useForm<OrderForm>({ resolver: zodResolver(orderSchema), defaultValues: emptyOrder() });
  const [setId, setSetId] = useState('');
  const [destination, setDestination] = useState('');
  const [problem, setProblem] = useState<OrderProblem | null>(null);
  const [placed, setPlaced] = useState(false);

  const done = (orders: Order[]) => { queryClient.setQueryData(key, orders); setProblem(null); };
  const failed = (e: unknown) => { setPlaced(false); setProblem(problemOf(e instanceof ApiError ? e.code : undefined)); };

  const add = useMutation({ mutationFn: (v: OrderForm) => addOrder(encounterId, v), onSuccess: o => { done(o); form.reset(emptyOrder()); setPlaced(false); }, onError: failed });
  const addSet = useMutation({ mutationFn: () => addOrderSet(encounterId, setId), onSuccess: o => { done(o); setSetId(''); setPlaced(false); }, onError: failed });
  const include = useMutation({ mutationFn: (v: { id: string; included: boolean }) => includeOrder(v.id, v.included), onSuccess: done, onError: failed });
  const place = useMutation({ mutationFn: () => placeOrders(encounterId, destination), onSuccess: o => { done(o); setPlaced(true); setDestination(''); }, onError: failed });

  const error = list.error instanceof ApiError ? list.error : null;
  return {
    orders: list.data ?? [],
    loading: list.isPending,
    /** Not this user's to see (the role, or no care relationship): the page shows nothing here. */
    hidden: error?.status === 403,
    failed: list.isError && error?.status !== 403,
    sets: sets.data ?? [],
    setId, onSetId: setSetId,
    form,
    destination, onDestination: setDestination,
    problem, placed,
    busy: add.isPending || addSet.isPending || include.isPending || place.isPending,
    onAdd: form.handleSubmit(v => add.mutate(v)),
    onAddSet: () => addSet.mutate(),
    onInclude: (id: string, included: boolean) => include.mutate({ id, included }),
    onPlace: () => place.mutate(),
  };
}

/** The investigation slip of a consultation: 404 means nothing has been placed yet. */
export function useSlipController(encounterId: string) {
  const slip = useQuery({ queryKey: ['order-slip', encounterId], queryFn: () => fetchSlip(encounterId), retry: false });
  const error = slip.error instanceof ApiError ? slip.error : null;
  const pdf = usePaperPdf(() => downloadSlipPdf(encounterId));
  return { slip: slip.data ?? null, loading: slip.isPending, nothingPlaced: error?.status === 404, failed: slip.isError && error?.status !== 404, onPrint: printPage, ...pdf };
}
