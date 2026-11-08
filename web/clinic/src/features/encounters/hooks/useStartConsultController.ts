import { useState } from 'react';
import { useMutation } from '@tanstack/react-query';
import type { AccessReason } from '../../clinical-access/domain/clinicalAccess';
import { requestAccess } from '../../clinical-access/services/clinicalAccessApi';
import { ApiError } from '../../schedule/services/scheduleApi';
import { startProblem, type StartProblem } from '../domain/encounter';
import { startEncounter } from '../services/encountersApi';

/**
 * "Start consultation" on one board card (MDV-P2-001). Success opens the consultation. Another open consultation asks the doctor to
 * return to it or leave it open; a missing care relationship offers "Request access" (MDV-P2-024). The server decides each case.
 */
export function useStartConsultController(appointmentId: string, patientId: string, onOpen: (encounterId: string) => void) {
  const [problem, setProblem] = useState<StartProblem | null>(null);
  const [reason, setReason] = useState<AccessReason>('SeeingPatientToday');
  const start = useMutation({
    mutationFn: (leaveOpen: boolean) => startEncounter(appointmentId, leaveOpen),
    onSuccess: e => { setProblem(null); onOpen(e.id); },
    onError: e => setProblem(startProblem(e instanceof ApiError ? e.status : undefined, e instanceof ApiError ? e.body : undefined)),
  });
  const ask = useMutation({
    mutationFn: () => requestAccess(patientId, reason),
    onSuccess: () => setProblem({ kind: 'refused', refusal: { canRequest: true, requestPending: true } }),
  });
  return {
    problem,
    starting: start.isPending,
    reason,
    onReason: setReason,
    asking: ask.isPending,
    askFailed: ask.isError,
    onStart: () => start.mutate(false),
    onLeaveOpenAndStart: () => start.mutate(true),
    onReturn: () => { if (problem?.kind === 'open') onOpen(problem.encounterId); },
    onRequestAccess: () => ask.mutate(),
  };
}
