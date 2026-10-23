import { useState } from 'react';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { asSearchError, toQuery, type DirectoryFilter, type SearchRequest } from '../domain/directory';
import { ApiError, searchPatients } from '../services/patientsApi';

export type LoadState = 'loading' | 'ready' | 'invalid' | 'forbidden' | 'notFound' | 'error';

/** 400/403/404 are answers, not outages: never retried. */
/** Memory only. Long enough that a remount (React strict mode, back navigation) reuses the request instead of auditing a second read. */
export const patientDataGcTime = 60_000;

export const noRetryOnAnswers = (failures: number, error: Error) => !(error instanceof ApiError && error.status < 500) && failures < 2;

export function loadState(query: { isPending: boolean; error: Error | null }): LoadState {
  if (query.isPending) return 'loading';
  if (!query.error) return 'ready';
  const status = query.error instanceof ApiError ? query.error.status : 0;
  return status === 400 ? 'invalid' : status === 403 ? 'forbidden' : status === 404 ? 'notFound' : 'error';
}

/**
 * Directory and search (MDV-P1-018). Searches run on submit, not per keystroke: each one is audited.
 * Results stay in memory only and are dropped a minute after nothing shows them; nothing goes to browser storage.
 */
export function usePatientDirectoryController() {
  const [text, setText] = useState('');
  const [request, setRequest] = useState<SearchRequest>({ query: null, filter: 'All', page: 1 });
  const search = useQuery({
    queryKey: ['patients', 'search', request],
    // No abort signal: a cancelled request has still been audited, and the remount would send (and audit) it again.
    queryFn: () => searchPatients(request),
    gcTime: patientDataGcTime,
    staleTime: Infinity, // each fetch is an audited read: reuse it until something changes it
    retry: noRetryOnAnswers,
    // The previous page stays on screen while the next loads, so the list and its pagination never unmount (focus is kept).
    placeholderData: keepPreviousData,
  });
  return {
    text,
    onTextChange: setText,
    onSubmit: () => setRequest(r => ({ ...r, query: toQuery(text), page: 1 })),
    filter: request.filter,
    onFilterChange: (filter: DirectoryFilter) => setRequest(r => ({ ...r, filter, page: 1 })),
    searched: request.query !== null,
    onPageChange: (page: number) => setRequest(r => ({ ...r, page })),
    result: search.data ?? null,
    updating: search.isPlaceholderData,
    state: loadState(search),
    errorCode: search.error instanceof ApiError ? asSearchError(search.error.code) : null,
    onRetry: () => void search.refetch(),
  };
}

