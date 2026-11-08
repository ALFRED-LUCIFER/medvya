import { fromCast } from '../../api/services/source';

export const fetchPatients = () => fromCast(c => c.patients);
export const fetchPatientRecord = (id: string) => fromCast(c => c.records[id] ?? null);
