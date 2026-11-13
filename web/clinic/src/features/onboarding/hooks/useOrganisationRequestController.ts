import { useState } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { ApiError } from '../../schedule/services/scheduleApi';
import { sessionQueryKey } from '../../session/hooks/useSessionController';
import {
  canSubmit, fileProblem, isEditable, itContactSchema, missingKinds, requestError, type DocumentKind, type FileProblem, type ItContactForm, type RequestError,
} from '../domain/organisationRequest';
import { fetchRequest, saveItContact, startNewRequest, submitRequest, uploadDocument } from '../services/organisationRequestApi';

const requestKey = ['organisation', 'request'];

/** The owner's onboarding request: documents, the hospital's IT contact and submit. The server decides who may and when; this follows its answer. */
export function useOrganisationRequestController() {
  const queryClient = useQueryClient();
  const request = useQuery({ queryKey: requestKey, queryFn: fetchRequest, retry: false });
  const data = request.data;
  // The shell's notice follows the state, so it is read again after every change of it.
  const refresh = () => Promise.all([queryClient.invalidateQueries({ queryKey: requestKey }), queryClient.invalidateQueries({ queryKey: sessionQueryKey })]);

  const [pickProblem, setPickProblem] = useState<{ kind: DocumentKind; problem: FileProblem } | null>(null);
  const upload = useMutation({ mutationFn: (v: { kind: DocumentKind; file: File }) => uploadDocument(v.kind, v.file), onSuccess: refresh });
  const form = useForm<ItContactForm>({ resolver: zodResolver(itContactSchema), values: { name: data?.itContactName ?? '', email: data?.itContactEmail ?? '' }, shouldFocusError: false }); // the error summary takes focus
  const contact = useMutation({ mutationFn: saveItContact, onSuccess: refresh });
  const submit = useMutation({ mutationFn: submitRequest, onSuccess: refresh });
  const startNew = useMutation({ mutationFn: startNewRequest, onSuccess: refresh });

  const failure = (e: unknown): RequestError | null => (e instanceof ApiError ? requestError(e.code) : e ? 'failed' : null);
  const uploadError: RequestError | FileProblem | null = pickProblem?.problem ?? failure(upload.error);

  return {
    request: data ?? null,
    loading: request.isPending,
    forbidden: request.error instanceof ApiError && request.error.status === 403,
    notFound: request.error instanceof ApiError && request.error.status === 404,
    loadFailed: request.isError && !(request.error instanceof ApiError && [403, 404].includes(request.error.status)),
    editable: data ? isEditable(data.state) : false,
    missing: data ? missingKinds(data) : [],
    ready: data ? canSubmit(data) : false,
    uploading: upload.isPending ? upload.variables?.kind ?? null : null,
    uploadError,
    uploadErrorKind: pickProblem?.kind ?? upload.variables?.kind ?? null,
    onFile: (kind: DocumentKind, file: File | undefined) => {
      if (!file) return;
      const problem = fileProblem(file);
      setPickProblem(problem ? { kind, problem } : null);
      if (!problem) upload.mutate({ kind, file });
    },
    form,
    savingContact: contact.isPending,
    contactSaved: contact.isSuccess && !form.formState.isDirty,
    contactFailed: contact.isError,
    onContact: form.handleSubmit(values => contact.mutate(values)),
    submitting: submit.isPending,
    submitFailed: submit.isError,
    onSubmit: () => submit.mutate(),
    startingNew: startNew.isPending,
    startNewFailed: startNew.isError,
    onStartNew: () => startNew.mutate(),
  };
}
