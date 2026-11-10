import { useState } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { ApiError } from '../../schedule/services/scheduleApi';
import { addendumFailure, addendumSchema, pdfFailure, type AddendumFailure, type AddendumForm } from '../domain/note';
import { addAddendum, downloadNotePdf, fetchNote, fetchNoteCatalog, printPage, startReauth } from '../services/noteApi';

const noteQuery = (encounterId: string) => ({ queryKey: ['note', encounterId], queryFn: () => fetchNote(encounterId), retry: false, staleTime: Infinity, refetchOnWindowFocus: false });

/**
 * A signed note's versions and its addendum composer (MDV-P2-013). The addendum is signed under the same re-authentication as the note; when the
 * server asks for it, the text stays on screen and the doctor chooses when to leave to confirm (the text is never put in browser storage).
 */
export function useAddendumController(encounterId: string, canSign: boolean) {
  const queryClient = useQueryClient();
  const note = useQuery(noteQuery(encounterId));
  const form = useForm<AddendumForm>({ resolver: zodResolver(addendumSchema), defaultValues: { text: '', reason: '' } });
  const [composing, setComposing] = useState(false);
  const [problem, setProblem] = useState<AddendumFailure | null>(null);

  const submit = form.handleSubmit(async values => {
    setProblem(null);
    try {
      queryClient.setQueryData(['note', encounterId], await addAddendum(encounterId, values));
      form.reset();
      setComposing(false);
    } catch (e) {
      const failure = e instanceof ApiError ? addendumFailure(e.status, e.body) : 'failed';
      if (failure === 'stale') void queryClient.invalidateQueries({ queryKey: ['note', encounterId] });
      setProblem(failure);
    }
  });

  return {
    signed: note.data?.status === 'Signed',
    signature: note.data?.signature ?? null,
    addenda: note.data?.addenda ?? [],
    canAdd: canSign,
    composing, problem,
    submitting: form.formState.isSubmitting,
    invalid: !!(form.formState.errors.text || form.formState.errors.reason),
    register: form.register,
    onCompose: () => { setComposing(true); setProblem(null); },
    onCancel: () => { setComposing(false); setProblem(null); form.reset(); },
    onSubmit: () => void submit(),
    onConfirmIdentity: async () => {
      try { await startReauth(); } catch { setProblem('failed'); }
    },
  };
}

/** The print view of a signed note (MDV-P2-013): the note, its template order, and the server PDF on request. */
export function useNotePrintController(encounterId: string) {
  const note = useQuery(noteQuery(encounterId));
  const catalog = useQuery({ queryKey: ['note-templates'], queryFn: fetchNoteCatalog, retry: false });
  const [downloading, setDownloading] = useState(false);
  const [pdfProblem, setPdfProblem] = useState<'unsupported' | 'failed' | null>(null);
  const template = catalog.data?.templates.find(t => t.key === note.data?.templateKey) ?? null;
  return {
    note: note.data?.status === 'Signed' ? note.data : null,
    template,
    loading: note.isPending || catalog.isPending,
    failed: note.isError || catalog.isError || (!!note.data && note.data.status !== 'Signed'),
    downloading, pdfProblem,
    onPrint: printPage,
    onPdf: async () => {
      setDownloading(true);
      setPdfProblem(null);
      try { await downloadNotePdf(encounterId); } catch (e) { setPdfProblem(pdfFailure(e instanceof ApiError ? e.status : undefined)); } finally { setDownloading(false); }
    },
  };
}
