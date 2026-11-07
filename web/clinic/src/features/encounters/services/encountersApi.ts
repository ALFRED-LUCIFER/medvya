import { post, send } from '../../schedule/services/scheduleApi';
import type { Encounter, EncounterBanner } from '../domain/encounter';

// Encounters (MDV-P2-001). Ids only in paths and bodies; the patient's identity comes back from the server, never goes out.
export const startEncounter = (appointmentId: string, leaveOpen: boolean) => post<Encounter>('/v1/encounters', { appointmentId, leaveOpen });
export const fetchEncounter = (id: string) => send<EncounterBanner>(`/v1/encounters/${id}`);
export const endEncounter = (id: string) => post<Encounter>(`/v1/encounters/${id}/end`, {});
