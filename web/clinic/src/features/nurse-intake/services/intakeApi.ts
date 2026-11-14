import { post, send, csrf } from '../../schedule/services/scheduleApi';
import type { AllergyForm, AllergyStatus, IntakeScreen, MedicineForm, IntakeView, VitalKind } from '../domain/intake';

// Nurse intake (MDV-P2-007). Ids only in paths; the readings and the reason go in bodies, never in a URL.
export const fetchIntake = (appointmentId: string) => send<IntakeScreen>(`/v1/intake/${appointmentId}`);

export const saveIntake = (appointmentId: string, body: { chiefComplaint: string; vitals: { kind: VitalKind; value: number }[] }) =>
  send<IntakeView>(`/v1/intake/${appointmentId}`, { method: 'PUT', headers: { ...csrf, 'Content-Type': 'application/json' }, body: JSON.stringify(body) });

export const keepVital = (appointmentId: string, kind: VitalKind) => post<IntakeView>(`/v1/intake/${appointmentId}/vitals/${kind}/keep`, {});
// MDV-P2-048: one confirmation for every flagged reading; a correction with its reason (in the body) after the handoff.
export const keepAllVitals = (appointmentId: string) => post<IntakeView>(`/v1/intake/${appointmentId}/vitals/keep-all`, {});
export const correctVital = (appointmentId: string, kind: VitalKind, value: number, reason: string) =>
  post<IntakeView>(`/v1/intake/${appointmentId}/vitals/${kind}/correct`, { value, reason });
export const escalateIntake = (appointmentId: string, reason: string) => post<IntakeView>(`/v1/intake/${appointmentId}/escalate`, { reason });
export const handOffIntake = (appointmentId: string) => post<IntakeView>(`/v1/intake/${appointmentId}/handoff`, {});

// Allergies and patient-reported medicines (MDV-P2-008).
export const addAllergy = (appointmentId: string, a: AllergyForm) =>
  post<IntakeView>(`/v1/intake/${appointmentId}/allergies`, { substance: a.substance, reaction: a.reaction, status: a.status });
export const setAllergyStatus = (appointmentId: string, allergyId: string, status: AllergyStatus) =>
  send<IntakeView>(`/v1/intake/${appointmentId}/allergies/${allergyId}/status`, { method: 'PUT', headers: { ...csrf, 'Content-Type': 'application/json' }, body: JSON.stringify({ status }) });
export const confirmNoKnownAllergies = (appointmentId: string) => post<IntakeView>(`/v1/intake/${appointmentId}/no-known-allergies`, {});
export const addMedicine = (appointmentId: string, m: MedicineForm) => post<IntakeView>(`/v1/intake/${appointmentId}/medicines`, m);
