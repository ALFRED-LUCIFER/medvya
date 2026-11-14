import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ApiError } from '../../schedule/services/scheduleApi';
import { sessionQueryKey } from '../../session/hooks/useSessionController';
import { fetchNurseIntake, saveNurseIntake } from '../services/settingsApi';

const key = ['settings', 'nurse-intake'];

/**
 * The nurse-intake switch (MDV-P2-007). It changes every board of the practice at once, so a flip first asks to confirm, with how many patients
 * are with the nurse now (read again as the dialog opens); only the confirm saves (MDV-P2-055). The session is then refetched so the Nurse menu
 * and the board's nurse step follow at once.
 */
export function useNurseIntakeController() {
  const queryClient = useQueryClient();
  const query = useQuery({ queryKey: key, queryFn: fetchNurseIntake });
  const [confirming, setConfirming] = useState<boolean | null>(null);
  const save = useMutation({
    mutationFn: saveNurseIntake,
    onSuccess: data => {
      setConfirming(null);
      queryClient.setQueryData(key, data);
      void queryClient.invalidateQueries({ queryKey: sessionQueryKey });
    },
  });
  return {
    enabled: query.data?.enabled ?? false,
    loading: query.isPending,
    forbidden: query.error instanceof ApiError && query.error.status === 403,
    loadFailed: query.isError,
    saving: save.isPending,
    saveFailed: save.isError,
    withNurse: query.data?.withNurse ?? null,
    /** The state the owner asked to switch to, while the dialog asks to confirm; null when closed. */
    confirming,
    onChange: (enabled: boolean) => { save.reset(); setConfirming(enabled); void query.refetch(); },
    onConfirm: () => { if (confirming !== null) save.mutate(confirming); },
    onCancel: () => { if (!save.isPending) { setConfirming(null); save.reset(); } },
  };
}
