import { post, send } from '../../api/services/apiClient';
import type { AllergyState } from '../../patient360/domain/chart';
import type { Elsewhere, Escalation, PendingNote } from '../domain/today';

// Today's allergy flags (MDV-P2-002). Patient ids go in the body, never the URL; a patient the clinician may not treat is absent from the answer.
export const fetchAllergyFlags = (patientIds: string[]) =>
  post<{ items: { patientId: string; state: AllergyState; abnormalVitals: boolean }[] }>('/v1/chart/allergy-flags', { patientIds });

/** The signed-in doctor's own unsigned notes at the session's facility, oldest first; the server decides whose and where. */
export const fetchPendingNotes = () => send<{ items: PendingNote[]; more: boolean }>('/v1/notes/pending');

/** The signed-in doctor's own visit counts today at the organisation's other locations they work at (#1365 F-061); the server decides which. */
export const fetchElsewhere = (facilityId: string) => send<Elsewhere[]>(`/v1/front-desk/elsewhere?facilityId=${encodeURIComponent(facilityId)}`);

/** Open nurse escalations (MDV-P2-007) of patients this doctor may treat at the facility, until the consultation starts. Ids in the query only. */
export const fetchEscalations = (facilityId: string) => send<Escalation[]>(`/v1/intake-escalations?facilityId=${encodeURIComponent(facilityId)}`);
