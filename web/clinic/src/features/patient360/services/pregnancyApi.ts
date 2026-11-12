import { csrf, send } from '../../schedule/services/scheduleApi';
import type { PregnancyOutcome, PregnancyStatus } from '../domain/chart';

// The pregnancy record (MDV-P2-034). Only the patient id is in the path; dates, counts and readings are in the body.
const post = (patientId: string, tail: string, body?: unknown) =>
  send<PregnancyStatus>(`/v1/patients/${patientId}/pregnancy${tail}`, {
    method: 'POST', headers: { ...csrf, 'Content-Type': 'application/json' }, body: body === undefined ? undefined : JSON.stringify(body),
  });

export const startPregnancy = (patientId: string, body: object) => post(patientId, '', body);
export const closePregnancy = (patientId: string, outcome: PregnancyOutcome) => post(patientId, '/close', { outcome });
export const counselDangerSigns = (patientId: string) => post(patientId, '/danger-signs');
export const ruleOutPregnancy = (patientId: string) => post(patientId, '/ruled-out');
export const recordPregnancyReadings = (patientId: string, body: object) => post(patientId, '/readings', body);
