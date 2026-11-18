import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { ApiError } from '../../api/services/apiClient';
import { errorKey, findingProblem, sortSurfaces, stepTarget, type AcceptanceMethod, type Dentition, type DentalView, type PlanItem, type ToothCondition } from '../domain/dental';
import { acceptEstimate, addPlanItem, downloadDentalPdf, fetchDental, fetchDentalServices, movePlanItem, recordFinding } from '../services/dentalApi';

const status = (e: unknown) => (e instanceof ApiError ? e.status : null);
const code = (e: unknown) => (e instanceof ApiError ? e.code : undefined);

/**
 * The Dental tab (MDV-P8-011, MDV-P8-012, DN-02 and DN-03): the chart, the picked tooth's history and a new finding, the plan with its next steps, the
 * estimate and the patient's acceptance, and the PDF. Every read is audited, so nothing refetches on focus; every write reloads the chart. A write that
 * lost a race (409) reloads and says so. Findings and starting or finishing an item need the doctor's open consultation with the patient (the server
 * says which); without one the screen says to start it, never fakes it.
 */
export function useDentalController(patientId: string, canChange: boolean): DentalView {
  const queryClient = useQueryClient();
  const key = ['dental', patientId];
  const chart = useQuery({ queryKey: key, queryFn: () => fetchDental(patientId), retry: false, refetchOnWindowFocus: false });
  const services = useQuery({ queryKey: ['dental-services', patientId], queryFn: () => fetchDentalServices(patientId), retry: false, refetchOnWindowFocus: false, enabled: canChange });
  const reload = () => queryClient.invalidateQueries({ queryKey: key });
  const encounterId = chart.data?.openEncounterId ?? null;

  const [dentition, setDentition] = useState<Dentition>('permanent');
  const [tooth, setTooth] = useState<string | null>(null);
  const [condition, setCondition] = useState<ToothCondition | null>(null);
  const [surfaces, setSurfaces] = useState<string>('');
  const [note, setNote] = useState('');
  const [added, setAdded] = useState<string | null>(null);
  const finding = useMutation({
    mutationFn: () => recordFinding(encounterId!, { tooth: tooth!, condition: condition!, surfaces, note }),
    onSuccess: () => { setAdded(tooth); setCondition(null); setSurfaces(''); setNote(''); void reload(); },
  });
  const findingLocal = tooth ? findingProblem(condition, surfaces) : null;

  const [serviceId, setServiceId] = useState('');
  const [phase, setPhase] = useState(1);
  const [planTooth, setPlanTooth] = useState<string | null>(null);
  const plan = useMutation({
    mutationFn: () => addPlanItem(patientId, { serviceId, tooth: planTooth, surfaces: '', phase }),
    onSuccess: () => { setServiceId(''); void reload(); },
  });

  const [declining, setDeclining] = useState<string | null>(null);
  const [reason, setReason] = useState('');
  const move = useMutation({
    mutationFn: ({ item, step }: { item: PlanItem; step: keyof typeof stepTarget }) =>
      movePlanItem(patientId, item.id, { to: stepTarget[step], reason: step === 'decline' ? reason : null, encounterId: step === 'start' || step === 'done' ? encounterId : null, version: item.version }),
    onSuccess: () => { setDeclining(null); setReason(''); void reload(); },
    onError: e => { if (status(e) === 409) void reload(); },
  });

  const [acceptedBy, setAcceptedBy] = useState('');
  const [method, setMethod] = useState<AcceptanceMethod>('OnTablet');
  const accept = useMutation({
    mutationFn: () => acceptEstimate(patientId, { acceptedBy, method }),
    onSuccess: () => { setAcceptedBy(''); void reload(); },
  });
  const pdf = useMutation({ mutationFn: () => downloadDentalPdf(patientId) });

  const d = chart.data;
  const selectedHistory = d && tooth ? d.history.filter(f => f.tooth === tooth) : [];
  return {
    loading: chart.isPending,
    unavailable: status(chart.error) === 404,
    forbidden: status(chart.error) === 403,
    failed: chart.isError && status(chart.error) !== 404 && status(chart.error) !== 403,
    chart: d ?? null,
    canChange,
    encounterId,
    dentition, onDentition: setDentition,
    tooth, onTooth: (t: string) => { setTooth(t); setPlanTooth(t); setAdded(null); finding.reset(); },
    current: d && tooth ? d.teeth.find(f => f.tooth === tooth) ?? null : null,
    history: selectedHistory,
    condition, onCondition: (c: ToothCondition) => { setCondition(c); if (!['Caries', 'Filling'].includes(c)) setSurfaces(''); },
    surfaces, onSurface: (s: string) => setSurfaces(sortSurfaces(surfaces.includes(s) ? surfaces.replace(s, '') : surfaces + s)),
    note, onNote: setNote,
    findingProblem: findingLocal,
    findingSaving: finding.isPending,
    findingError: finding.isError ? errorKey(code(finding.error)) : null,
    findingFailed: finding.isError && !errorKey(code(finding.error)),
    findingAdded: added,
    onAddFinding: () => { if (!findingLocal && encounterId && tooth) finding.mutate(); },

    services: services.data ?? [],
    servicesLoading: services.isPending && canChange,
    serviceId, onService: setServiceId,
    phase, onPhase: setPhase,
    planTooth, onPlanTooth: setPlanTooth,
    planSaving: plan.isPending,
    planError: plan.isError ? errorKey(code(plan.error)) : null,
    planFailed: plan.isError && !errorKey(code(plan.error)),
    onAddItem: () => { if (serviceId) plan.mutate(); },

    declining, reason, onReason: setReason,
    onDecline: (id: string | null) => { setDeclining(id); setReason(''); move.reset(); },
    moving: move.isPending ? move.variables?.item.id ?? null : null,
    moveConflict: status(move.error) === 409,
    moveError: move.isError ? errorKey(code(move.error)) : null,
    moveFailed: move.isError && status(move.error) !== 409 && !errorKey(code(move.error)),
    onStep: (item: PlanItem, step: keyof typeof stepTarget) => move.mutate({ item, step }),

    acceptedBy, onAcceptedBy: setAcceptedBy, method, onMethod: setMethod,
    accepting: accept.isPending,
    acceptError: accept.isError ? errorKey(code(accept.error)) : null,
    acceptFailed: accept.isError && !errorKey(code(accept.error)),
    acceptDone: accept.isSuccess,
    onAccept: () => accept.mutate(),

    printing: pdf.isPending, printFailed: pdf.isError, onPrint: () => pdf.mutate(),
  };
}

