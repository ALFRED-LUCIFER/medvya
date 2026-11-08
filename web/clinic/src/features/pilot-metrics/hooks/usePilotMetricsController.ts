import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { ApiError } from '../../schedule/services/scheduleApi';
import { baselineBody, baselineSchema, checkInGap, deliveryPercent, type BaselineInput } from '../domain/pilotMetrics';
import { fetchDelivery, fetchPilotMetrics, recordBaseline } from '../services/pilotMetricsApi';

/**
 * Settings → Pilot metrics (MDV-P1-041): the last two weeks' check-in time, no-shows, refused double bookings and message delivery
 * beside the clinic's recorded baselines, and the form to record a baseline. The server decides who may see it.
 */
export function usePilotMetricsController(facilityId: string) {
  const queryClient = useQueryClient();
  const key = ['pilot-metrics', facilityId];
  const metrics = useQuery({ queryKey: key, queryFn: () => fetchPilotMetrics(facilityId) });
  // Delivery has its own permission (Register / book patients): without it the rest of the report still shows.
  const delivery = useQuery({ queryKey: ['notifications', 'delivery', facilityId], queryFn: () => fetchDelivery(facilityId), retry: false });
  const form = useForm<BaselineInput>({
    resolver: zodResolver(baselineSchema),
    defaultValues: { metric: 'NoShowPercent', value: '', method: '', fromDate: '', toDate: '' },
  });
  const save = useMutation({
    mutationFn: (values: BaselineInput) => recordBaseline(baselineBody(facilityId, values)),
    onSuccess: () => { form.reset({ ...form.getValues(), value: '', method: '' }); void queryClient.invalidateQueries({ queryKey: key }); },
  });

  return {
    state: metrics.isPending ? ('loading' as const)
      : metrics.error instanceof ApiError && metrics.error.status === 403 ? ('forbidden' as const)
      : metrics.isError ? ('error' as const) : ('ready' as const),
    metrics: metrics.data ?? null,
    gapSeconds: metrics.data ? checkInGap(metrics.data.checkIn) : null,
    delivery: delivery.data ? deliveryPercent(delivery.data) : null,
    deliveryFailed: delivery.isError,
    deliveryForbidden: delivery.error instanceof ApiError && delivery.error.status === 403,
    form,
    saving: save.isPending,
    saveFailed: save.isError,
    saved: save.isSuccess,
    onSubmit: form.handleSubmit(values => save.mutate(values)),
  };
}
