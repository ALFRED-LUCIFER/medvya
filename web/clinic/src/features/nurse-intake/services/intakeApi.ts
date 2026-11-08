import { post, send, csrf } from '../../schedule/services/scheduleApi';
import type { IntakeScreen, IntakeView, VitalKind } from '../domain/intake';

// Nurse intake (MDV-P2-007). Ids only in paths; the readings and the reason go in bodies, never in a URL.
export const fetchIntake = (appointmentId: string) => send<IntakeScreen>(`/v1/intake/${appointmentId}`);

export const saveIntake = (appointmentId: string, body: { chiefComplaint: string; vitals: { kind: VitalKind; value: number }[] }) =>
  send<IntakeView>(`/v1/intake/${appointmentId}`, { method: 'PUT', headers: { ...csrf, 'Content-Type': 'application/json' }, body: JSON.stringify(body) });

export const keepVital = (appointmentId: string, kind: VitalKind) => post<IntakeView>(`/v1/intake/${appointmentId}/vitals/${kind}/keep`, {});
export const escalateIntake = (appointmentId: string, reason: string) => post<IntakeView>(`/v1/intake/${appointmentId}/escalate`, { reason });
export const handOffIntake = (appointmentId: string) => post<IntakeView>(`/v1/intake/${appointmentId}/handoff`, {});
