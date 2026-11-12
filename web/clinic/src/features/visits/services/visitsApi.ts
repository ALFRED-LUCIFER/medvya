import { send } from '../../schedule/services/scheduleApi';
import type { DeskVisit, VisitsAnswer } from '../domain/visits';

// The patient id is the only identifier in a path; the facility id is an opaque id. Opening either list is an audited read.
export const fetchVisits = (patientId: string) => send<VisitsAnswer>(`/v1/patients/${patientId}/visits`);
export const fetchDeskVisits = async (facilityId: string, patientId: string) =>
  (await send<{ visits: DeskVisit[] }>(`/v1/schedule/patients/${patientId}/visits?${new URLSearchParams({ facilityId })}`)).visits;
