import { useState } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { ApiError } from '../../api/services/apiClient';
import { displaySchema, type DisplayInput } from '../domain/queueTv';
import type { CreatedDisplay } from '../domain/queueTv';
import { copyText, createDisplay, displayLink, fetchDisplays, newDisplayLink, revokeDisplay } from '../services/queueTvApi';

/**
 * Queue displays of the active facility (MDV-P1-031): add a TV and get its link once, replace a lost link (#1413), or revoke one. Staff who configure the
 * facility only; the server checks that, this screen only follows it.
 */
export function useDisplaysController(facilityId: string) {
  const queryClient = useQueryClient();
  const key = ['queue-displays', facilityId];
  const list = useQuery({ queryKey: key, queryFn: () => fetchDisplays(facilityId) });
  const form = useForm<DisplayInput>({ resolver: zodResolver(displaySchema), defaultValues: { name: '' } });
  // The link last issued, by Add or New link; only in memory, and gone when the page is left.
  const [shown, setShown] = useState<{ created: CreatedDisplay; replaced: boolean } | null>(null);
  const add = useMutation({
    mutationFn: (values: DisplayInput) => createDisplay(facilityId, values.name.trim()),
    onSuccess: created => { setShown({ created, replaced: false }); form.reset({ name: '' }); void queryClient.invalidateQueries({ queryKey: key }); },
  });
  const relink = useMutation({ mutationFn: newDisplayLink, onSuccess: created => setShown({ created, replaced: true }) });
  const revoke = useMutation({
    mutationFn: revokeDisplay,
    onSuccess: (_, id) => { setShown(s => (s?.created.display.id === id ? null : s)); void queryClient.invalidateQueries({ queryKey: key }); },
  });
  const [copied, setCopied] = useState(false);
  const link = shown ? displayLink(shown.created.token) : null;

  return {
    form,
    displays: list.data ?? [],
    loading: list.isPending,
    forbidden: list.error instanceof ApiError && list.error.status === 403,
    loadFailed: list.isError,
    adding: add.isPending,
    addFailed: add.isError,
    /** Shown once, right after Add or New link: only a hash of its token is kept. */
    created: shown ? { name: shown.created.display.name, link: link!, replaced: shown.replaced } : null,
    copied,
    revokingId: revoke.isPending ? revoke.variables : null,
    revokeFailed: revoke.isError,
    relinkingId: relink.isPending ? relink.variables : null,
    relinkFailed: relink.isError,
    onSubmit: form.handleSubmit(values => { setCopied(false); add.mutate(values); }),
    onCopy: () => link && void copyText(link).then(() => setCopied(true)),
    onRevoke: (id: string) => revoke.mutate(id),
    onNewLink: (id: string) => { setCopied(false); relink.mutate(id); },
  };
}

export type DisplaysViewModel = ReturnType<typeof useDisplaysController>;
