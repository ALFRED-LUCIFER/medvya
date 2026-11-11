import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ApiError } from '../../schedule/services/scheduleApi';
import { printPage } from '../../encounters/services/noteApi';
import { pdfProblem, type SecondLanguage } from '../domain/authorisation';
import { approveLayout, downloadRxPdf, fetchLayout, fetchRxPrint, verifyPaper, verifyRx } from '../services/prescriptionApi';

/**
 * The printed prescription (MDV-P2-017): the authorised prescription from the server (a 409 means not authorised, changed or cancelled since), the second
 * print language, the server PDF on A4 or A5, and the doctor's own approval of the layout.
 */
export function useRxPrintController(encounterId: string) {
  const queryClient = useQueryClient();
  const print = useQuery({ queryKey: ['prescription-print', encounterId], queryFn: () => fetchRxPrint(encounterId), retry: false, refetchOnWindowFocus: false });
  const layout = useQuery({ queryKey: ['prescription-layout'], queryFn: fetchLayout, retry: false });
  const approve = useMutation({ mutationFn: approveLayout, onSuccess: l => queryClient.setQueryData(['prescription-layout'], l) });
  const [second, setSecond] = useState<SecondLanguage>('none');
  const [downloading, setDownloading] = useState<'A4' | 'A5' | null>(null);
  const [problem, setProblem] = useState<'unsupported' | 'failed' | null>(null);
  const error = print.error instanceof ApiError ? print.error : null;
  return {
    print: print.data ?? null,
    loading: print.isPending,
    notAuthorised: error?.status === 409,
    cancelled: error?.code === 'cancelled',
    failed: print.isError && error?.status !== 409,
    second, onSecond: setSecond,
    onPrint: printPage,
    downloading, pdfProblem: problem,
    onPdf: async (paper: 'A4' | 'A5') => {
      setDownloading(paper);
      setProblem(null);
      try { await downloadRxPdf(encounterId, paper); } catch (e) { setProblem(pdfProblem(e instanceof ApiError ? e.status : undefined)); } finally { setDownloading(null); }
    },
    layout: layout.data ?? null,
    approving: approve.isPending,
    layoutFailed: approve.isError,
    onApprove: () => approve.mutate(),
  };
}

/** The public QR check (MDV-P2-017): validity only. A 404 is a code that matches no prescription. */
export function useRxVerifyController(token: string) {
  const check = useQuery({ queryKey: ['rx-verify', token], queryFn: () => verifyRx(token), retry: false, refetchOnWindowFocus: false });
  const error = check.error instanceof ApiError ? check.error : null;
  return { result: check.data ?? null, loading: check.isPending, unknown: error?.status === 404, failed: check.isError && error?.status !== 404 };
}

/** A server paper's PDF button (slip, letter): one download at a time, and a 422 (text no PDF font has) told apart from any other failure. */
export function usePaperPdf(download: () => Promise<void>) {
  const [downloading, setDownloading] = useState(false);
  const [problem, setProblem] = useState<'unsupported' | 'failed' | null>(null);
  return {
    downloading, pdfProblem: problem,
    onPdf: async () => {
      setDownloading(true);
      setProblem(null);
      try { await download(); } catch (e) { setProblem(pdfProblem(e instanceof ApiError ? e.status : undefined)); } finally { setDownloading(false); }
    },
  };
}

/** The public QR check of a slip or letter (MDV-P2-018/019). A 404 is a code that matches no paper. */
export function usePaperVerifyController(token: string) {
  const check = useQuery({ queryKey: ['paper-verify', token], queryFn: () => verifyPaper(token), retry: false, refetchOnWindowFocus: false });
  const error = check.error instanceof ApiError ? check.error : null;
  return { result: check.data ?? null, loading: check.isPending, unknown: error?.status === 404, failed: check.isError && error?.status !== 404 };
}
