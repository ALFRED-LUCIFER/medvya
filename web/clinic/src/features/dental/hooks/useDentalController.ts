import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useI18n } from '@medvya/i18n';
import { ApiError } from '../../api/services/apiClient';
import { errorKey, findingProblem, historyComplete, historyOnVisit, sortSurfaces, stepTarget, type AcceptanceMethod, type Dentition, type DentalView, type HistoryQuestion, type PlanItem, type ToothCondition } from '../domain/dental';
import { acceptEstimate, addPlanItem, confirmHistory, downloadConsentPdf, downloadDentalPdf, fetchDental, fetchDentalServices, movePlanItem, recordFinding, signConsent, withdrawConsent } from '../services/dentalApi';

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

  // MDV-P8-019: the consent is read and signed in the language the app shows (the tablet's language switch changes it), and recorded with it.
  const { locale } = useI18n();
  const [consenting, setConsenting] = useState<string | null>(null);
  const [consentSigner, setConsentSigner] = useState('');
  const [signature, setSignature] = useState<string | null>(null);
  const consent = useMutation({
    mutationFn: () => signConsent(patientId, consenting!, { encounterId: encounterId!, language: locale, signedBy: consentSigner, signature: signature!.replace(/^data:image\/png;base64,/, '') }),
    onSuccess: () => { setConsenting(null); setConsentSigner(''); setSignature(null); void reload(); },
  });
  const [withdrawing, setWithdrawing] = useState<string | null>(null);
  const [withdrawReason, setWithdrawReason] = useState('');
  const withdraw = useMutation({
    mutationFn: () => withdrawConsent(patientId, withdrawing!, withdrawReason),
    onSuccess: () => { setWithdrawing(null); setWithdrawReason(''); void reload(); },
  });
  const consentPdf = useMutation({ mutationFn: (consentId: string) => downloadConsentPdf(patientId, consentId) });

  // MDV-P8-014: the history check, answered with the patient on this visit; nothing is pre-filled from the last check (each answer is asked again).
  const [answers, setAnswers] = useState<Partial<Record<HistoryQuestion, boolean>>>({});
  const [historyNote, setHistoryNote] = useState('');
  const [historyTried, setHistoryTried] = useState(false);
  const history = useMutation({
    mutationFn: () => confirmHistory(encounterId!, { answers, note: historyNote }),
    onSuccess: () => { setAnswers({}); setHistoryNote(''); setHistoryTried(false); void reload(); },
  });

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

    answers, onAnswer: (q: HistoryQuestion, yes: boolean) => setAnswers(a => ({ ...a, [q]: yes })),
    historyNote, onHistoryNote: setHistoryNote,
    historyOnVisit: d ? historyOnVisit(d) : false,
    historySaving: history.isPending,
    historyFailed: history.isError,
    historyIncomplete: historyTried && !historyComplete(answers),
    historyDone: history.isSuccess,
    onConfirmHistory: () => { setHistoryTried(true); if (encounterId && historyComplete(answers)) history.mutate(); },

    consenting, onConsenting: (id: string | null) => { setConsenting(id); setConsentSigner(''); setSignature(null); consent.reset(); },
    consentSigner, onConsentSigner: setConsentSigner,
    onSignature: setSignature, hasSignature: !!signature,
    consentSaving: consent.isPending,
    consentError: consent.isError ? errorKey(code(consent.error)) : null,
    consentFailed: consent.isError && !errorKey(code(consent.error)),
    onSignConsent: () => { if (consenting && encounterId && signature && consentSigner.trim()) consent.mutate(); },
    withdrawing, onWithdrawing: (id: string | null) => { setWithdrawing(id); setWithdrawReason(''); withdraw.reset(); },
    withdrawReason, onWithdrawReason: setWithdrawReason,
    withdrawSaving: withdraw.isPending, withdrawFailed: withdraw.isError,
    onWithdraw: () => { if (withdrawing && withdrawReason.trim()) withdraw.mutate(); },
    onConsentPdf: (id: string) => consentPdf.mutate(id), consentPdfFailed: consentPdf.isError,
  };
}

