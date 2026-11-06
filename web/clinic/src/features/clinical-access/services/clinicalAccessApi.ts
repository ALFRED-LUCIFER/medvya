import { csrf, post, send } from '../../schedule/services/scheduleApi';
import type { AccessQueue, ClinicalPolicy } from '../domain/clinicalAccess';

// The owner's side of request access, and the clinic policy (MDV-P2-024). Ids in paths; the facility is the session's, never sent.
export const fetchAccessQueue = () => send<AccessQueue>('/v1/access-requests');
export const grantAccess = (id: string, days: number) => post<unknown>(`/v1/access-requests/${id}/grant`, { days });
export const declineAccess = (id: string) => post<unknown>(`/v1/access-requests/${id}/decline`, {});

export const fetchClinicalPolicy = () => send<{ policy: ClinicalPolicy }>('/v1/settings/clinical-access');
export const saveClinicalPolicy = (policy: ClinicalPolicy) =>
  send<{ policy: ClinicalPolicy }>('/v1/settings/clinical-access', { method: 'PUT', headers: { ...csrf, 'Content-Type': 'application/json' }, body: JSON.stringify({ policy }) });
