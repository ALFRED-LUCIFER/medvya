import { apiFetch } from '../../api/services/apiClient';
import { fail, jsonHeaders } from '../../patients/services/patientsApi';
import type { MergeAction, MergeHistoryEntry, MergeQueueRow, MergeReview } from '../domain/merge';

const base = '/v1/patients';
const paths: Record<MergeAction, string> = { notSame: 'not-same', requestApproval: 'request-approval', approve: 'approve', undo: 'undo' };

export async function fetchMergeQueue(): Promise<MergeQueueRow[]> {
  const res = await apiFetch(`${base}/merge-requests`, { credentials: 'same-origin' });
  if (!res.ok) return fail(res);
  return ((await res.json()) as { requests: MergeQueueRow[] }).requests;
}

export async function fetchMergeReview(id: string): Promise<MergeReview> {
  const res = await apiFetch(`${base}/merge-requests/${encodeURIComponent(id)}`, { credentials: 'same-origin' });
  if (!res.ok) return fail(res);
  return res.json() as Promise<MergeReview>;
}

export async function fetchMergeHistory(patientId: string): Promise<MergeHistoryEntry[]> {
  const res = await apiFetch(`${base}/${encodeURIComponent(patientId)}/merges`, { credentials: 'same-origin' });
  if (!res.ok) return fail(res);
  return ((await res.json()) as { merges: MergeHistoryEntry[] }).merges;
}

/** Each step is a POST that the server audits with the actor; the id is opaque and nothing else is sent. */
export async function decideMerge(id: string, action: MergeAction): Promise<void> {
  const res = await apiFetch(`${base}/merge-requests/${encodeURIComponent(id)}/${paths[action]}`, {
    method: 'POST', credentials: 'same-origin', headers: jsonHeaders, body: '{}',
  });
  if (!res.ok) return fail(res);
}

/** Asks for a review of two records (MDV-P1-017). Opaque ids only; the server audits it and refuses a pair already decided. */
export async function requestMerge(patientId: string, otherPatientId: string): Promise<string> {
  const res = await apiFetch(`${base}/merge-requests`, {
    method: 'POST', credentials: 'same-origin', headers: jsonHeaders, body: JSON.stringify({ patientId, otherPatientId }),
  });
  if (!res.ok) return fail(res);
  return ((await res.json()) as { id: string }).id;
}
