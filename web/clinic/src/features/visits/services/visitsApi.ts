import { send } from '../../schedule/services/scheduleApi';
import type { DeskRecordVisits, VisitsAnswer } from '../domain/visits';

// The patient id is the only identifier in a path; the facility id is an opaque id. Opening either list is an audited read.
export const fetchVisits = (patientId: string) => send<VisitsAnswer>(`/v1/patients/${patientId}/visits`);
export const fetchDeskVisits = (facilityId: string, patientId: string) =>
  send<DeskRecordVisits>(`/v1/schedule/patients/${patientId}/visits?${new URLSearchParams({ facilityId })}`);
