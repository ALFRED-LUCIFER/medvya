import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { asSearchError, toQuery } from '../domain/directory';
import { ApiError, searchPatients } from '../services/patientsApi';
import { loadState, noRetryOnAnswers, patientDataGcTime } from './usePatientDirectoryController';

/**
 * Choosing a patient with front-desk search (MDV-P1-018) inside another screen (booking, walk-in): runs on submit, not per
 * keystroke (each search is audited), with the directory's caching and retry rules. Rows are privacy-safe until one is picked.
 */
export function usePatientSearch(onPick: (id: string) => void) {
  const [text, setText] = useState('');
  const [query, setQuery] = useState<string | null>(null);
  const search = useQuery({
    queryKey: ['patients', 'search', { query, filter: 'All', page: 1 }],
    queryFn: () => searchPatients({ query, filter: 'All', page: 1 }),
    enabled: query !== null,
    gcTime: patientDataGcTime,
    staleTime: Infinity, // as the directory: an audited search is reused, not refetched
    retry: noRetryOnAnswers,
  });
  return {
    view: {
      text,
      onText: setText,
      onSubmit: () => setQuery(toQuery(text)),
      state: query === null ? ('idle' as const) : loadState(search),
      result: search.data ?? null,
      errorCode: search.error instanceof ApiError ? asSearchError(search.error.code) : null,
      onPick,
    },
    clear: () => { setText(''); setQuery(null); },
  };
}
