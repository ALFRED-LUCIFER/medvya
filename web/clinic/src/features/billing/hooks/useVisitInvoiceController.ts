import { useQuery } from '@tanstack/react-query';
import { ApiError } from '../../api/services/apiClient';
import { invoiceProblem } from '../domain/invoice';
import { openVisitInvoice } from '../services/invoicesApi';

/** The visit's open bill, created as a draft with its suggested consultation the first time (the server makes it once, whoever asks). */
export function useVisitInvoiceController(appointmentId: string) {
  const open = useQuery({ queryKey: ['billing', 'visit', appointmentId], queryFn: () => openVisitInvoice(appointmentId), retry: false, gcTime: 0 });
  return {
    invoiceId: open.data?.id ?? null,
    problem: open.isError ? invoiceProblem(open.error instanceof ApiError ? (open.error.status === 404 ? 'visit:missing' : open.error.code) : undefined) : null,
  };
}
