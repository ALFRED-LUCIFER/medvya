import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ApiError } from '../../schedule/services/scheduleApi';
import { sessionQueryKey } from '../../session/hooks/useSessionController';
import { fetchNurseIntake, saveNurseIntake } from '../services/settingsApi';

const key = ['settings', 'nurse-intake'];

/** The nurse-intake switch (MDV-P2-007). Saved on change; the session is refetched so the Nurse menu and the board's nurse step follow at once. */
export function useNurseIntakeController() {
  const queryClient = useQueryClient();
  const query = useQuery({ queryKey: key, queryFn: fetchNurseIntake });
  const save = useMutation({
    mutationFn: saveNurseIntake,
    onSuccess: data => {
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
    onChange: (enabled: boolean) => save.mutate(enabled),
  };
}
