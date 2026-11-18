import { useMutation, useQueryClient } from '@tanstack/react-query';
import { ApiError, post } from '../../api/services/apiClient';
import type { StarterView } from '../domain/dental';

type StarterLoad = { added: number; skipped: number };

/**
 * The dental services starter list (MDV-P8-010): one press loads the procedures the clinic does not have yet (by code), all or nothing; a second
 * press adds nothing. The services list is refetched so Settings › Services shows them at once.
 */
export function useDentalStarterController(): StarterView {
  const queryClient = useQueryClient();
  const load = useMutation({
    mutationFn: () => post<StarterLoad>('/v1/settings/services/dental-starter', {}),
    onSuccess: () => { void queryClient.invalidateQueries({ queryKey: ['billing', 'services'] }); },
  });
  return {
    loading: load.isPending,
    result: load.data ?? null,
    unsupported: load.error instanceof ApiError && load.error.code === 'country:unsupported',
    failed: load.isError && !(load.error instanceof ApiError && load.error.code === 'country:unsupported'),
    onLoad: () => load.mutate(),
  };
}
