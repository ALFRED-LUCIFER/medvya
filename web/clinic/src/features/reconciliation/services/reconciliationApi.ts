import { post, send } from '../../schedule/services/scheduleApi';
import type { DecisionKind, Reconciliation, TaperStep } from '../domain/reconciliation';

// Medication reconciliation (MDV-P2-011). Only the encounter id is in a path; a medicine's name, dose and the reason go in bodies, never a URL.
const base = (encounterId: string) => `/v1/encounters/${encounterId}/medicines/reconciliation`;
export const fetchReconciliation = (encounterId: string) => send<Reconciliation>(base(encounterId));
export const decideMedicine = (encounterId: string, d: { key: string; kind: DecisionKind; newDose: string; reason: string; steps?: TaperStep[] }) => post<Reconciliation>(`${base(encounterId)}/decisions`, d);
export const confirmReconciliation = (encounterId: string) => post<Reconciliation>(`${base(encounterId)}/confirm`, {});
