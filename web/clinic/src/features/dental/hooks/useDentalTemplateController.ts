import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { csrf, send } from '../../api/services/apiClient';
import { dentalTemplateDefault, type DentalTemplateView } from '../domain/dental';

const url = '/v1/settings/note-templates/';

/**
 * Settings › Dental: whether the clinic's new notes start on the dental template (MDV-P8-015). The clinic default is a list of templates on the
 * server; on means the dental template only, off means General OPD (a doctor's own choice still wins).
 */
export function useDentalTemplateController(): DentalTemplateView {
  const queryClient = useQueryClient();
  const current = useQuery({ queryKey: ['settings', 'note-templates'], queryFn: () => send<string[]>(url) });
  const save = useMutation({
    mutationFn: (on: boolean) => send<string[]>(url, { method: 'PUT', headers: { 'Content-Type': 'application/json', ...csrf }, body: JSON.stringify({ keys: dentalTemplateDefault(on) }) }),
    onSuccess: keys => queryClient.setQueryData(['settings', 'note-templates'], keys),
  });
  return {
    loading: current.isPending,
    loadFailed: current.isError,
    on: current.data?.includes('dental') ?? false,
    saving: save.isPending,
    saveFailed: save.isError,
    onChange: on => save.mutate(on),
  };
}
