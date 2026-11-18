import { post, send } from '../../api/services/apiClient';
import { downloadPaperPdf } from '../../prescription/services/prescriptionApi';
import type { Acceptance, AcceptanceMethod, Consent, DentalChart, DentalService, HistoryCheck, HistoryQuestion, PlanItem, PlanStatus, ToothCondition, ToothFinding } from '../domain/dental';

// The dental chart and plan (MDV-P8-011, MDV-P8-012). Ids in paths; teeth, conditions, notes and reasons travel in bodies, never in a URL.
export const fetchDental = (patientId: string) => send<DentalChart>(`/v1/patients/${patientId}/dental`);
export const fetchDentalServices = (patientId: string) => send<DentalService[]>(`/v1/patients/${patientId}/dental/services`);
export const recordFinding = (encounterId: string, body: { tooth: string; condition: ToothCondition; surfaces: string; note: string }) =>
  post<ToothFinding>(`/v1/encounters/${encounterId}/dental/findings`, body);
export const addPlanItem = (patientId: string, body: { serviceId: string; tooth: string | null; surfaces: string; phase: number }) =>
  post<PlanItem>(`/v1/patients/${patientId}/dental/plan`, body);
export const movePlanItem = (patientId: string, itemId: string, body: { to: PlanStatus; reason: string | null; encounterId: string | null; version: number }) =>
  post<PlanItem>(`/v1/patients/${patientId}/dental/plan/${itemId}/move`, body);
export const acceptEstimate = (patientId: string, body: { acceptedBy: string; method: AcceptanceMethod }) =>
  post<Acceptance>(`/v1/patients/${patientId}/dental/estimate/acceptance`, body);
export const downloadDentalPdf = (patientId: string) => downloadPaperPdf(`/v1/patients/${patientId}/dental/pdf`, 'dental-chart-and-estimate.pdf');
export const confirmHistory = (encounterId: string, body: { answers: Partial<Record<HistoryQuestion, boolean>>; note: string }) =>
  post<HistoryCheck>(`/v1/encounters/${encounterId}/dental/history-check`, body);
// MDV-P8-019: the signature travels as a base64 PNG in the body; the form PDF is saved under a name with no patient data.
export const signConsent = (patientId: string, itemId: string, body: { encounterId: string; language: string; signedBy: string; signature: string }) =>
  post<Consent>(`/v1/patients/${patientId}/dental/plan/${itemId}/consent`, body);
export const withdrawConsent = (patientId: string, consentId: string, reason: string) =>
  post<Consent>(`/v1/patients/${patientId}/dental/consents/${consentId}/withdraw`, { reason });
export const downloadConsentPdf = (patientId: string, consentId: string) => downloadPaperPdf(`/v1/patients/${patientId}/dental/consents/${consentId}/pdf`, 'dental-consent.pdf');
export const markStage = (patientId: string, itemId: string, body: { encounterId: string; stage: number }) =>
  post<PlanItem>(`/v1/patients/${patientId}/dental/plan/${itemId}/stages`, body);
