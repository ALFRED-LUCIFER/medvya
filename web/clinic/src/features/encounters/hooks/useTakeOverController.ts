import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { takeOverSchema } from '../domain/note';
import { takeOverVisit } from '../services/encountersApi';

/**
 * Take over this visit (#1365): a doctor who does not lead an unsigned visit gives a reason and becomes its lead clinician, then signs as
 * usual. The server decides who may; afterwards the visit is read again, so Sign unlocks from what the server says.
 */
export function useTakeOverController(encounterId: string) {
  const queryClient = useQueryClient();
  const form = useForm<{ reason: string }>({ resolver: zodResolver(takeOverSchema), defaultValues: { reason: '' } });
  const [open, setOpen] = useState(false);
  const take = useMutation({
    mutationFn: (reason: string) => takeOverVisit(encounterId, reason),
    onSuccess: () => { setOpen(false); return queryClient.invalidateQueries({ queryKey: ['encounter', encounterId] }); },
  });
  return {
    open, busy: take.isPending, failed: take.isError, reasonMissing: !!form.formState.errors.reason,
    register: form.register('reason'),
    onOpenChange: (next: boolean) => { if (take.isPending) return; setOpen(next); if (!next) { form.reset(); take.reset(); } },
    onSubmit: form.handleSubmit(v => take.mutate(v.reason.trim())),
  };
}
