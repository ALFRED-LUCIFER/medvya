import { csrf, post, send } from '../../schedule/services/scheduleApi';
import type { CareGoal, CareTarget, ChronicCare, TargetCode } from '../domain/chronicCare';

const put = <T>(url: string, body: unknown) => send<T>(url, { method: 'PUT', headers: { ...csrf, 'Content-Type': 'application/json' }, body: JSON.stringify(body) });

// Chronic care (MDV-P2-038). The patient and goal ids are the only things in the path; the measure, values and text travel in the body.
export const fetchChronicCare = (patientId: string) => send<ChronicCare>(`/v1/patients/${patientId}/chronic-care`);
export const setCareTarget = (patientId: string, code: TargetCode, limit: number) => put<CareTarget>(`/v1/patients/${patientId}/care-targets`, { code, limit });
export const recordCareCheck = (patientId: string, kind: string, doneOn: string) => post<void>(`/v1/patients/${patientId}/care-checks`, { kind, doneOn });
export const addCareGoal = (patientId: string, body: { text: string; target: string | null; progressPercent: number }) => post<CareGoal>(`/v1/patients/${patientId}/care-goals`, body);
export const updateCareGoal = (patientId: string, goalId: string, progressPercent: number) => put<CareGoal>(`/v1/patients/${patientId}/care-goals/${goalId}`, { progressPercent });
