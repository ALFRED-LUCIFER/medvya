import { useState } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { unfinishedMedicines } from '../../prescription/domain/authorisation';
import { overrideSchema } from '../../prescription/domain/checks';
import { discardDraft, fetchAuthorisation, fetchPrescription } from '../../prescription/services/prescriptionApi';
import { fetchOrders } from '../../orders/services/ordersApi';

export type FinishAct = 'end' | 'sign';

/**
 * MDV-P1-090: End consultation and Sign note ask first when the prescription is an unfinished draft or orders are not placed. The same queries as
 * the prescription and orders panels (shared cache), so the question follows what the doctor just did. A role that cannot see them is asked nothing:
 * the server decides either way. Authorise now goes to the prescription (authorising stays its own act with its own step-up); Discard records a reason
 * on the server; End anyway (or Sign anyway) carries on with nothing changed.
 */
export function useFinishController(encounterId: string, acts: Record<FinishAct, () => void>) {
  const queryClient = useQueryClient();
  const lines = useQuery({ queryKey: ['prescription', encounterId], queryFn: () => fetchPrescription(encounterId), retry: false });
  const authKey = ['prescription-authorisation', encounterId];
  const state = useQuery({ queryKey: authKey, queryFn: () => fetchAuthorisation(encounterId), enabled: lines.isSuccess, retry: false });
  const orders = useQuery({ queryKey: ['orders', encounterId], queryFn: () => fetchOrders(encounterId), retry: false });
  const medicines = unfinishedMedicines(lines.data ?? [], state.data ?? null);
  const unplaced = (orders.data ?? []).filter(o => o.state === 'Draft').map(o => o.name);
  const [asking, setAsking] = useState<FinishAct | null>(null);
  const [discarding, setDiscarding] = useState(false);
  const form = useForm<{ reason: string }>({ resolver: zodResolver(overrideSchema), defaultValues: { reason: '' } });
  const close = () => { setAsking(null); setDiscarding(false); form.reset({ reason: '' }); };
  const discard = useMutation({
    mutationFn: (reason: string) => discardDraft(encounterId, reason),
    onSuccess: s => {
      queryClient.setQueryData(authKey, s);
      const act = asking;
      close();
      if (act) acts[act]();
    },
  });
  const request = (act: FinishAct) => () => (medicines.length > 0 || unplaced.length > 0 ? setAsking(act) : acts[act]());
  return {
    asking, medicines, unplaced, discarding, form, discardPending: discard.isPending, discardFailed: discard.isError,
    onRequestEnd: request('end'),
    onRequestSign: request('sign'),
    onCancel: close,
    onDiscarding: (open: boolean) => { setDiscarding(open); form.reset({ reason: '' }); discard.reset(); },
    onDiscard: form.handleSubmit(v => discard.mutate(v.reason)),
    onAnyway: () => { const act = asking; close(); if (act) acts[act](); },
  };
}

export type FinishViewModel = ReturnType<typeof useFinishController>;
