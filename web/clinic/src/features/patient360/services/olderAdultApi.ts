import { post, send } from '../../schedule/services/scheduleApi';
import type { GeriatricHistory, MedicineReview, toRecord } from '../domain/olderAdult';

// The older adult review (MDV-P2-037). The patient id is the only thing in the path; scores travel in the body.
export const fetchGeriatric = (patientId: string) => send<GeriatricHistory>(`/v1/patients/${patientId}/geriatric-assessments`);
export const recordGeriatric = (patientId: string, body: ReturnType<typeof toRecord>) => post<unknown>(`/v1/patients/${patientId}/geriatric-assessments`, body);
export const fetchMedicineReview = (patientId: string) => send<MedicineReview>(`/v1/patients/${patientId}/medicine-review`);
