import { post } from '../../schedule/services/scheduleApi';
import type { AllergyState } from '../../patient360/domain/chart';

// Today's allergy flags (MDV-P2-002). Patient ids go in the body, never the URL; a patient the clinician may not treat is absent from the answer.
export const fetchAllergyFlags = (patientIds: string[]) =>
  post<{ items: { patientId: string; state: AllergyState }[] }>('/v1/chart/allergy-flags', { patientIds });
