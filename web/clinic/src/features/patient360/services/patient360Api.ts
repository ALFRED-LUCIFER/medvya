import { send } from '../../schedule/services/scheduleApi';
import type { Patient360 } from '../domain/chart';

// Patient 360 (MDV-P2-004). The patient id is the only thing in the path; the identity comes back from the server.
export const fetchPatient360 = (patientId: string) => send<Patient360>(`/v1/patients/${patientId}/chart`);
