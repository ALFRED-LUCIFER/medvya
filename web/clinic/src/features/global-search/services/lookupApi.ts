import { send } from '../../api/services/apiClient';
import type { LookupResult } from '../domain/lookup';

/** The search text travels in the POST body, never in the URL (names, phones and IDs are PHI). */
export const lookupPatients = (query: string, signal?: AbortSignal) =>
  send<LookupResult>('/v1/patients/lookup', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ query }), signal });
