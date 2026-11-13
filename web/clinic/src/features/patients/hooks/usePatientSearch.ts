import { useEffect, useState } from 'react';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { asSearchError, toQuery, typeaheadDelayMs, typeaheadMinChars } from '../domain/directory';
import { ApiError, searchPatients } from '../services/patientsApi';
import { loadState, patientDataGcTime } from './usePatientDirectoryController';

/** What is searched: a typed query, or (query null) the whole list a page at a time. No request at all until one of them is asked for. */
type Asked = { query: string | null; page: number } | null;

/**
 * Choosing a patient with front-desk search (MDV-P1-018) inside another screen (booking, walk-in, waitlist, merge, access log).
 * Results appear while typing (MDV-AUDIT-1100): a short pause after the last key, from two characters, each search audited and
 * rate-limited; Enter and the Search button still run it at once. "Show all patients" lists everyone a page at a time. Rows carry the
 * full name; the search text goes in the POST body only, and nothing is kept outside memory.
 */
export function usePatientSearch(onPick: (id: string) => void) {
  const [text, setText] = useState('');
  const [asked, setAsked] = useState<Asked>(null);
  const typed = toQuery(text);

  useEffect(() => {
    if (typed === null || typed.length < typeaheadMinChars) return;
    const timer = setTimeout(() => setAsked(a => (a?.query === typed && a.page === 1 ? a : { query: typed, page: 1 })), typeaheadDelayMs);
    return () => clearTimeout(timer);
  }, [typed]);

  const search = useQuery({
    queryKey: ['patients', 'search', { query: asked?.query ?? null, filter: 'All', page: asked?.page ?? 1 }],
    queryFn: () => searchPatients({ query: asked!.query, filter: 'All', page: asked!.page }),
    enabled: asked !== null,
    gcTime: patientDataGcTime,
    staleTime: Infinity, // as the directory: an audited search is reused, not refetched
    placeholderData: keepPreviousData, // the list stays while the next one loads, so it does not flicker at every pause in typing
  });
  return {
    view: {
      text,
      onText: (value: string) => {
        setText(value);
        // Clearing the box ends a typed search; the full list stays, since the desk asked for it.
        if (toQuery(value) === null) setAsked(a => (a?.query === null ? a : null));
      },
      onSubmit: () => setAsked({ query: typed, page: 1 }),
      onBrowseAll: () => setAsked({ query: null, page: 1 }),
      browsing: asked !== null && asked.query === null,
      page: asked?.page ?? 1,
      onPage: (page: number) => setAsked(a => (a ? { ...a, page } : a)),
      updating: search.isPlaceholderData,
      state: asked === null ? ('idle' as const) : loadState(search),
      result: search.data ?? null,
      errorCode: search.error instanceof ApiError ? asSearchError(search.error.code) : null,
      onPick,
    },
    clear: () => { setText(''); setAsked(null); },
  };
}
