import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ApiError } from '../../api/services/apiClient';
import { sessionQueryKey } from '../../session/hooks/useSessionController';
import { fetchFeatureSwitch, saveFeatureSwitch, type FeatureSwitchName } from '../services/settingsApi';

/**
 * A practice-wide switch: nurse intake (MDV-P2-007) or billing (MDV-P4-002, #1430). It changes every board of the practice at once, so a flip
 * first asks to confirm, with how many patients are with the nurse now for nurse intake (read again as the dialog opens); only the confirm saves
 * (MDV-P2-055). The session is then refetched so the menu (Nurse, Billing) and the board follow at once.
 */
export function useFeatureSwitchController(name: FeatureSwitchName) {
  const key = ['settings', name];
  const queryClient = useQueryClient();
  const query = useQuery({ queryKey: key, queryFn: () => fetchFeatureSwitch(name) });
  const [confirming, setConfirming] = useState<boolean | null>(null);
  const save = useMutation({
    mutationFn: (enabled: boolean) => saveFeatureSwitch(name, enabled),
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
    /** Medvya has not released the feature to this practice (MDV-P5-041): the server has no switch for it, so none is shown. */
    unavailable: query.error instanceof ApiError && query.error.status === 404,
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
