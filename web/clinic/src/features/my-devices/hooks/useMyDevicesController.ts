import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { orderDevices } from '../domain/devices';
import { endDevice, fetchDevices } from '../services/devicesApi';

/** My devices (MDV-P0-012): the user's live sessions and a Sign out per other device. The server ends the session and audits it. */
export function useMyDevicesController() {
  const queryClient = useQueryClient();
  const query = useQuery({ queryKey: ['me', 'sessions'], queryFn: fetchDevices });
  const end = useMutation({ mutationFn: endDevice, onSettled: () => void queryClient.invalidateQueries({ queryKey: ['me', 'sessions'] }) });
  return {
    devices: orderDevices(query.data ?? []),
    loading: query.isPending,
    failed: query.isError,
    endingId: end.isPending ? end.variables : null,
    endFailed: end.isError,
    ended: end.isSuccess,
    onEnd: (id: string) => end.mutate(id),
  };
}
