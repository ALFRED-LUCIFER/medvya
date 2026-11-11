import { useQuery } from '@tanstack/react-query';
import { fetchDocuments } from '../services/documentsApi';

/** One patient's documents, list only, for the chart's right-hand column (MDV-P2-033); shares the cache with the documents page. Audited, so no refetch on focus. */
export function useDocumentListController(patientId: string) {
  const q = useQuery({ queryKey: ['documents', patientId], queryFn: () => fetchDocuments(patientId), retry: false, refetchOnWindowFocus: false });
  return { documents: q.data?.documents ?? [], loading: q.isPending, failed: q.isError };
}
