import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import type { DismissReason, FollowUpDue } from '../domain/followUpDue';
import { dismissFollowUp } from '../services/frontDeskApi';

type Dismissing = { followUp: FollowUpDue; name: string };

/**
 * Dismissing the doctor's follow-up from the board's Done card or the desk record (MDV-P1-102): a reason from the list, then confirm. The board and the
 * desk record refetch after it, so the line goes everywhere. Closing the dialog changes nothing.
 */
export function useFollowUpDueController() {
  const queryClient = useQueryClient();
  const [dismissing, setDismissing] = useState<Dismissing | null>(null);
  const [reason, setReason] = useState<DismissReason | null>(null);
  const [missing, setMissing] = useState(false);
  const dismiss = useMutation({
    mutationFn: ({ id, why }: { id: string; why: DismissReason }) => dismissFollowUp(id, why),
    onSuccess: () => {
      setDismissing(null);
      void queryClient.invalidateQueries({ queryKey: ['front-desk'] });
      void queryClient.invalidateQueries({ queryKey: ['desk-visits'] });
    },
  });
  const close = () => { setDismissing(null); setReason(null); setMissing(false); dismiss.reset(); };
  return {
    dismissing,
    reason,
    reasonMissing: missing,
    working: dismiss.isPending,
    failed: dismiss.isError,
    onStart: (followUp: FollowUpDue, name: string) => { close(); setDismissing({ followUp, name }); },
    onReason: (r: DismissReason) => { setReason(r); setMissing(false); },
    onConfirm: () => {
      if (!dismissing) return;
      if (!reason) { setMissing(true); return; }
      dismiss.mutate({ id: dismissing.followUp.id, why: reason });
    },
    onCancel: () => { if (!dismiss.isPending) close(); },
  };
}

export type FollowUpDueViewModel = ReturnType<typeof useFollowUpDueController>;
