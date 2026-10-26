import { useState } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { ApiError } from '../../schedule/services/scheduleApi';
import { displaySchema, type DisplayInput } from '../domain/queueTv';
import { copyText, createDisplay, displayLink, fetchDisplays, revokeDisplay } from '../services/queueTvApi';

/**
 * Queue displays of the active facility (MDV-P1-031): add a TV and get its link once, or revoke one. Staff who configure the
 * facility only; the server checks that, this screen only follows it.
 */
export function useDisplaysController(facilityId: string) {
  const queryClient = useQueryClient();
  const key = ['queue-displays', facilityId];
  const list = useQuery({ queryKey: key, queryFn: () => fetchDisplays(facilityId), retry: (n, e) => !(e instanceof ApiError && e.status === 403) && n < 2 });
  const form = useForm<DisplayInput>({ resolver: zodResolver(displaySchema), defaultValues: { name: '' } });
  const add = useMutation({
    mutationFn: (values: DisplayInput) => createDisplay(facilityId, values.name.trim()),
    onSuccess: () => { form.reset({ name: '' }); void queryClient.invalidateQueries({ queryKey: key }); },
  });
  const revoke = useMutation({ mutationFn: revokeDisplay, onSuccess: () => void queryClient.invalidateQueries({ queryKey: key }) });
  const [copied, setCopied] = useState(false);
  const link = add.data ? displayLink(add.data.token) : null;

  return {
    form,
    displays: list.data ?? [],
    loading: list.isPending,
    forbidden: list.error instanceof ApiError && list.error.status === 403,
    loadFailed: list.isError,
    adding: add.isPending,
    addFailed: add.isError,
    /** Shown once, right after adding: only a hash of its token is kept. */
    created: add.data ? { name: add.data.display.name, link: link! } : null,
    copied,
    revokingId: revoke.isPending ? revoke.variables : null,
    revokeFailed: revoke.isError,
    onSubmit: form.handleSubmit(values => { setCopied(false); add.mutate(values); }),
    onCopy: () => link && void copyText(link).then(() => setCopied(true)),
    onRevoke: (id: string) => revoke.mutate(id),
  };
}

export type DisplaysViewModel = ReturnType<typeof useDisplaysController>;
