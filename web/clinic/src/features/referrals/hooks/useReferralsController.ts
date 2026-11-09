import { useState } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { fetchDocuments } from '../../documents/services/documentsApi';
import { ApiError } from '../../schedule/services/scheduleApi';
import {
  advanceSchema, emptyReferral, fileInSchema, problemOf, referralSchema,
  type AdvanceForm, type Channel, type FileInForm, type Referral, type ReferralForm, type ReferralProblem,
} from '../domain/referrals';
import { advanceReferral, createReferral, fetchLetter, fetchReferrals, fileReferralIn, printPage, sendReferral } from '../services/referralsApi';

/**
 * The referrals of one patient (MDV-P2-019), under a consultation. The server decides who may write: a refusal (403) of the list hides the panel.
 * Every call answers with the referral as it is now, so the screen never works out a state itself.
 */
export function useReferralsController(patientId: string, encounterId: string) {
  const queryClient = useQueryClient();
  const key = ['referrals', patientId];
  const list = useQuery({ queryKey: key, queryFn: () => fetchReferrals(patientId), retry: false });
  const documents = useQuery({ queryKey: ['referral-documents', patientId], queryFn: () => fetchDocuments(patientId), enabled: list.isSuccess, retry: false });
  const createForm = useForm<ReferralForm>({ resolver: zodResolver(referralSchema), defaultValues: emptyReferral() });
  const fileForm = useForm<FileInForm>({ resolver: zodResolver(fileInSchema), defaultValues: { provider: '', reason: '', documentId: '' } });
  const advanceForm = useForm<AdvanceForm>({ resolver: zodResolver(advanceSchema), defaultValues: { state: 'Closed', documentId: '', note: '' } });
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [channel, setChannel] = useState<Channel>('Print');
  const [problem, setProblem] = useState<ReferralProblem | null>(null);

  const changed = (r: Referral) => {
    queryClient.setQueryData<Referral[]>(key, old => [r, ...(old ?? []).filter(x => x.id !== r.id)].sort((a, b) => Number(b.open) - Number(a.open)));
    setProblem(null);
  };
  const failed = (e: unknown) => setProblem(problemOf(e instanceof ApiError ? e.code : undefined));

  const create = useMutation({ mutationFn: (v: ReferralForm) => createReferral(encounterId, v), onSuccess: r => { changed(r); createForm.reset(emptyReferral()); setSelectedId(r.id); }, onError: failed });
  const fileIn = useMutation({ mutationFn: (v: FileInForm) => fileReferralIn(patientId, v), onSuccess: r => { changed(r); fileForm.reset(); }, onError: failed });
  const send = useMutation({ mutationFn: (id: string) => sendReferral(id, channel), onSuccess: changed, onError: failed });
  const advance = useMutation({ mutationFn: (v: { id: string; form: AdvanceForm }) => advanceReferral(v.id, v.form), onSuccess: r => { changed(r); advanceForm.reset({ state: 'Closed', documentId: '', note: '' }); }, onError: failed });

  const error = list.error instanceof ApiError ? list.error : null;
  const referrals = list.data ?? [];
  const selected = referrals.find(r => r.id === selectedId) ?? null;
  return {
    referrals, loading: list.isPending, hidden: error?.status === 403, failed: list.isError && error?.status !== 403,
    documents: documents.data?.documents.filter(d => d.scanStatus === 'Clean') ?? [],
    createForm, fileForm, advanceForm,
    selected, onSelect: (id: string | null) => { setSelectedId(id); setProblem(null); },
    channel, onChannel: setChannel,
    problem,
    busy: create.isPending || fileIn.isPending || send.isPending || advance.isPending,
    onCreate: createForm.handleSubmit(v => create.mutate(v)),
    onFileIn: fileForm.handleSubmit(v => fileIn.mutate(v)),
    onSend: () => selected && send.mutate(selected.id),
    onAdvance: advanceForm.handleSubmit(v => selected && advance.mutate({ id: selected.id, form: v })),
  };
}

/** Patient 360's open referrals: the same list, read only; a role that may not see it shows nothing. */
export function useOpenReferralsController(patientId: string) {
  const list = useQuery({ queryKey: ['referrals', patientId], queryFn: () => fetchReferrals(patientId), retry: false });
  const error = list.error instanceof ApiError ? list.error : null;
  return { open: (list.data ?? []).filter(r => r.open), loading: list.isPending, hidden: error?.status === 403 || error?.status === 404, failed: list.isError && error?.status !== 403 && error?.status !== 404 };
}

/** The letter of one referral out, printable. */
export function useLetterController(referralId: string) {
  const letter = useQuery({ queryKey: ['referral-letter', referralId], queryFn: () => fetchLetter(referralId), retry: false });
  const error = letter.error instanceof ApiError ? letter.error : null;
  return { letter: letter.data ?? null, loading: letter.isPending, notFound: error?.status === 404, failed: letter.isError && error?.status !== 404, onPrint: printPage };
}
