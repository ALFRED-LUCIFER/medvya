import { post, send } from '../../schedule/services/scheduleApi';
import type { ChildHealth, MilestoneAnswer, MilestoneAnswerer, MilestoneKey } from '../domain/childHealth';
import type { Growth, growthBody } from '../domain/growth';

// Immunisations and milestones (MDV-P2-036). The patient id is the only thing in a path; vaccines, batches, reasons and answers go in bodies.
export const fetchChildHealth = (patientId: string) => send<ChildHealth>(`/v1/patients/${patientId}/child-health`);
export const recordDose = (patientId: string, body: unknown) => post<ChildHealth>(`/v1/patients/${patientId}/immunisations`, body);
export const answerMilestone = (patientId: string, body: { band: string; milestone: MilestoneKey; answer: MilestoneAnswer; answeredBy: MilestoneAnswerer }) =>
  post<ChildHealth>(`/v1/patients/${patientId}/milestones`, body);

// Growth (MDV-P2-035): measurements go in the body, never the address.
export const fetchGrowth = (patientId: string) => send<Growth>(`/v1/patients/${patientId}/growth`);
export const recordGrowth = (patientId: string, body: ReturnType<typeof growthBody>) => post<Growth>(`/v1/patients/${patientId}/growth`, body);
