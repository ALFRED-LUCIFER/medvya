import { csrf, post, send } from '../../schedule/services/scheduleApi';
import type { AccessQueue, AccessReason, ClinicalPolicy } from '../domain/clinicalAccess';

// Request access, the owner's side of it, and the clinic policy (MDV-P2-024). Ids in paths; the facility is the session's, never sent.
export const requestAccess = (patientId: string, reason: AccessReason) => post<{ id: string; status: string }>(`/v1/patients/${patientId}/access-requests`, { reason });
export const fetchAccessQueue = () => send<AccessQueue>('/v1/access-requests');
export const grantAccess = (id: string, days: number) => post<unknown>(`/v1/access-requests/${id}/grant`, { days });
export const declineAccess = (id: string) => post<unknown>(`/v1/access-requests/${id}/decline`, {});

export const fetchClinicalPolicy = () => send<{ policy: ClinicalPolicy }>('/v1/settings/clinical-access');
export const saveClinicalPolicy = (policy: ClinicalPolicy) =>
  send<{ policy: ClinicalPolicy }>('/v1/settings/clinical-access', { method: 'PUT', headers: { ...csrf, 'Content-Type': 'application/json' }, body: JSON.stringify({ policy }) });
