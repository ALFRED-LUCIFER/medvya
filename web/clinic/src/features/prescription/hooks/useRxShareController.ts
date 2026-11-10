import { useState } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { ApiError } from '../../schedule/services/scheduleApi';
import {
  codeSchema, dispensedSchema, emptyShare, openProblem, shareProblem, shareSchema, toDispensedBody,
  type CodeForm, type DispensedForm, type OpenProblem, type RxShare, type ShareForm, type ShareProblem,
} from '../domain/share';
import { fetchShares, openSharedRx, recordDispatch, sendSharedCode, shareRx } from '../services/prescriptionApi';

/**
 * Sharing the authorised prescription (MDV-P2-020): every share with its state, a new share to the patient or a pharmacy, and the pharmacy's
 * dispatch (dispensed or cancelled). Every call answers with the whole list as it now stands.
 */
export function useRxShareController(encounterId: string) {
  const queryClient = useQueryClient();
  const key = ['prescription-shares', encounterId];
  const list = useQuery({ queryKey: key, queryFn: () => fetchShares(encounterId), retry: false });
  const shareForm = useForm<ShareForm>({ resolver: zodResolver(shareSchema), defaultValues: emptyShare() });
  const dispensedForm = useForm<DispensedForm>({ resolver: zodResolver(dispensedSchema), defaultValues: { pharmacyRef: '', dispensedAt: '' } });
  const [problem, setProblem] = useState<ShareProblem | null>(null);
  const [sent, setSent] = useState(false);
  const [dispensing, setDispensing] = useState<string | null>(null);
  const done = (shares: RxShare[]) => { queryClient.setQueryData(key, shares); setProblem(null); };
  const failed = (e: unknown) => { setSent(false); setProblem(shareProblem(e instanceof ApiError ? e.code : undefined)); };

  const share = useMutation({ mutationFn: (f: ShareForm) => shareRx(encounterId, f), onSuccess: s => { done(s); setSent(true); shareForm.reset(emptyShare()); }, onError: failed });
  const dispatch = useMutation({
    mutationFn: (v: { id: string; body: Parameters<typeof recordDispatch>[1] }) => recordDispatch(v.id, v.body),
    onSuccess: s => { done(s); setDispensing(null); dispensedForm.reset(); }, onError: failed,
  });
  return {
    shares: list.data ?? [], loading: list.isPending, failed: list.isError,
    shareForm, dispensedForm, problem, sent,
    busy: share.isPending || dispatch.isPending,
    onShare: shareForm.handleSubmit(f => { setSent(false); share.mutate(f); }),
    dispensing, onDispensing: setDispensing,
    onDispensed: dispensedForm.handleSubmit(f => dispensing && dispatch.mutate({ id: dispensing, body: toDispensedBody(f) })),
    onCancelDispatch: (id: string) => dispatch.mutate({ id, body: { state: 'Cancelled' } }),
  };
}

/** The public link page (MDV-P2-020): ask for the code (sent to the patient's registered mobile), then open the PDF with it. */
export function useSharedRxController(token: string) {
  const form = useForm<CodeForm>({ resolver: zodResolver(codeSchema), defaultValues: { code: '' } });
  const [problem, setProblem] = useState<OpenProblem | null>(null);
  const failed = (e: unknown) => setProblem(openProblem(e instanceof ApiError ? e.status : undefined, e instanceof ApiError ? e.code : undefined));
  const code = useMutation({ mutationFn: () => sendSharedCode(token), onSuccess: () => setProblem(null), onError: failed });
  const open = useMutation({ mutationFn: (f: CodeForm) => openSharedRx(token, f.code), onSuccess: () => setProblem(null), onError: failed });
  return {
    form, problem,
    codeSent: code.isSuccess, sending: code.isPending, opening: open.isPending, opened: open.isSuccess,
    onSendCode: () => code.mutate(),
    onOpen: form.handleSubmit(f => open.mutate(f)),
  };
}
