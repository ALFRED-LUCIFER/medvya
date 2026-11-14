import { useQuery } from '@tanstack/react-query';
import { fetchTokenSlip } from '../services/frontDeskApi';
import { ApiError } from '../../schedule/services/scheduleApi';
import { printPage } from '../../orders/services/ordersApi';

/** The token slip of one visit (MDV-P2-046): print or reprint the same token; a void token (409) says so instead. */
export function useTokenSlipController(appointmentId: string) {
  const slip = useQuery({ queryKey: ['token-slip', appointmentId], queryFn: () => fetchTokenSlip(appointmentId), retry: false });
  const error = slip.error instanceof ApiError ? slip.error : null;
  return {
    slip: slip.data ?? null,
    loading: slip.isPending,
    problem: !slip.isError ? null : error?.status === 409 ? 'void' as const : error?.status === 403 ? 'forbidden' as const : 'failed' as const,
    onPrint: printPage,
  };
}
