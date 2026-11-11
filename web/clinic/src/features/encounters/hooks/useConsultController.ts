import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ApiError } from '../../schedule/services/scheduleApi';
import { refusalOf } from '../../clinical-access/domain/clinicalAccess';
import { fetchPatient360 } from '../../patient360/services/patient360Api';
import type { ConsultView, StudioPanel } from '../domain/encounter';
import { loadConsultView, saveConsultView } from '../services/consultViewStore';
import { endEncounter, fetchEncounter } from '../services/encountersApi';

/**
 * One consultation (MDV-P2-001): its identity banner, and ending it (the visit moves on to Billing on the board). MDV-P2-041: which view the
 * doctor works in (one page or the guided steps, the same note either way) and the one page's side panel; the chart summary is read (and
 * audited) only when its tab is opened.
 */
export function useConsultController(encounterId: string) {
  const queryClient = useQueryClient();
  const key = ['encounter', encounterId];
  const query = useQuery({ queryKey: key, queryFn: () => fetchEncounter(encounterId), retry: false });
  const [view, setView] = useState<ConsultView>(loadConsultView);
  const [panel, setPanel] = useState<StudioPanel>('assist');
  const patientId = query.data?.patient.id ?? '';
  const chart = useQuery({ queryKey: ['patient360', patientId], queryFn: () => fetchPatient360(patientId), retry: false, refetchOnWindowFocus: false, enabled: view === 'page' && panel === 'chart' && !!patientId });
  const end = useMutation({ mutationFn: () => endEncounter(encounterId), onSuccess: () => void queryClient.invalidateQueries({ queryKey: key }) });
  const error = query.error instanceof ApiError ? query.error : null;
  return {
    banner: query.data ?? null,
    loading: query.isPending,
    notFound: error?.status === 404,
    refusal: error?.status === 403 ? (refusalOf(error.body) ?? { canRequest: false, requestPending: false }) : null,
    failed: query.isError,
    ending: end.isPending,
    endFailed: end.isError,
    onEnd: () => end.mutate(),
    view, onView: (v: ConsultView) => { setView(v); saveConsultView(v); },
    panel, onPanel: setPanel,
    chart: { data: chart.data?.chart ?? null, loading: chart.isPending, failed: chart.isError },
  };
}
