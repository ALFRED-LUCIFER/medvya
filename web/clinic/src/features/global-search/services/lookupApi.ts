import { csrf, send } from '../../schedule/services/scheduleApi';
import type { LookupResult } from '../domain/lookup';

/** The search text travels in the POST body, never in the URL (names, phones and IDs are PHI). */
export const lookupPatients = (query: string, signal?: AbortSignal) =>
  send<LookupResult>('/v1/patients/lookup', { method: 'POST', headers: { ...csrf, 'Content-Type': 'application/json' }, body: JSON.stringify({ query }), signal });
