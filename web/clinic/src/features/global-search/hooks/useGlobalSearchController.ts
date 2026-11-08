import { useEffect, useState, type KeyboardEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router';
import type { AccessReason } from '../../clinical-access/domain/clinicalAccess';
import { requestAccess } from '../../clinical-access/services/clinicalAccessApi';
import { patientDataGcTime } from '../../patients/hooks/usePatientDirectoryController';
import { ApiError } from '../../schedule/services/scheduleApi';
import { isShortcut, moveActive, searchableText, type LookupRow } from '../domain/lookup';
import { lookupPatients } from '../services/lookupApi';

/** Typing pauses this long before a search runs: every search is audited, so half-typed words are not searched. */
const pauseMs = 400;

/**
 * ⌘K / Ctrl+K patient search (MDV-P2-003, D-02) from any screen. Results are privacy-safe rows; the server says per row whether the chart
 * opens or access must be requested (care relationship, MDV-P2-024). Enter opens the highlighted chart; closing forgets the search.
 */
export function useGlobalSearchController(recordPath: (patientId: string) => string) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [open, setOpen] = useState(false);
  const [text, setText] = useState('');
  const [searched, setSearched] = useState<string | null>(null);
  const [active, setActive] = useState(-1);
  const [reason, setReason] = useState<AccessReason>('SeeingPatientToday');
  const [requested, setRequested] = useState<ReadonlySet<string>>(new Set());

  useEffect(() => {
    const onKey = (e: globalThis.KeyboardEvent) => {
      if (!isShortcut(e)) return;
      e.preventDefault();
      setOpen(o => !o);
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

  useEffect(() => {
    const timer = setTimeout(() => setSearched(searchableText(text)), pauseMs);
    return () => clearTimeout(timer);
  }, [text]);

  const query = useQuery({
    queryKey: ['global-search', searched],
    queryFn: ({ signal }) => lookupPatients(searched!, signal),
    enabled: open && searched !== null,
    gcTime: patientDataGcTime,
    staleTime: Infinity, // an audited search is reused, not repeated
    retry: false,
  });
  // Stale rows are not shown for newer text, and a request made here shows as pending at once.
  const waiting = searchableText(text) !== searched || query.isFetching;
  const rows: LookupRow[] = (searched !== null && query.data ? query.data.items : []).map(r => (requested.has(r.id) ? { ...r, access: 'RequestPending' } : r));
  const current = active >= 0 && active < rows.length ? rows[active]! : null;

  const close = () => {
    setOpen(false);
    setText('');
    setSearched(null);
    setActive(-1);
    queryClient.removeQueries({ queryKey: ['global-search'] });
  };
  const ask = useMutation({
    mutationFn: (patientId: string) => requestAccess(patientId, reason),
    onSuccess: (_, patientId) => setRequested(old => new Set(old).add(patientId)),
  });
  const openChart = (row: LookupRow) => {
    close();
    void navigate(recordPath(row.id));
  };

  return {
    open,
    onOpenChange: (o: boolean) => (o ? setOpen(true) : close()),
    text,
    onText: (t: string) => { setText(t); setActive(-1); },
    rows,
    active,
    onActive: setActive,
    current,
    total: query.data?.total ?? 0,
    /** What the live region announces: nothing typed yet, searching, how many found, or that the search failed. */
    status: searchableText(text) === null ? ('idle' as const) : query.isError ? ('failed' as const) : waiting ? ('searching' as const) : rows.length === 0 ? ('none' as const) : ('found' as const),
    reason,
    onReason: setReason,
    asking: ask.isPending,
    askFailed: ask.isError && !(ask.error instanceof ApiError && ask.error.status === 409),
    onRequestAccess: (row: LookupRow) => ask.mutate(row.id),
    onOpenChart: openChart,
    onClose: close,
    onKeyDown: (e: KeyboardEvent) => {
      if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
        e.preventDefault();
        setActive(moveActive(active, e.key, rows.length));
      } else if (e.key === 'Enter' && current?.access === 'Open') {
        e.preventDefault();
        openChart(current);
      }
    },
  };
}

export type GlobalSearchViewModel = ReturnType<typeof useGlobalSearchController>;
