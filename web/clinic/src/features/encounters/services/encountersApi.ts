import { csrf, post, send } from '../../schedule/services/scheduleApi';
import type { Encounter, EncounterBanner } from '../domain/encounter';
import type { FollowUp, FollowUpPeriod } from '../domain/followUp';

// Encounters (MDV-P2-001). Ids only in paths and bodies; the patient's identity comes back from the server, never goes out.
export const startEncounter = (appointmentId: string, leaveOpen: boolean) => post<Encounter>('/v1/encounters', { appointmentId, leaveOpen });
export const fetchEncounter = (id: string) => send<EncounterBanner>(`/v1/encounters/${id}`);
export const endEncounter = (id: string) => post<Encounter>(`/v1/encounters/${id}/end`, {});
// The plan's follow-up (MDV-P2-043): a period code in the body.
export const fetchFollowUp = (id: string) => send<FollowUp>(`/v1/encounters/${id}/follow-up`);
export const saveFollowUp = (id: string, period: FollowUpPeriod | null) =>
  send<FollowUp>(`/v1/encounters/${id}/follow-up`, { method: 'PUT', headers: { ...csrf, 'Content-Type': 'application/json' }, body: JSON.stringify({ period }) });
