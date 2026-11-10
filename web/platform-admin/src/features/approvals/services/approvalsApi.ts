import { ApiError, getJson, post } from '../../api/services/api';
import type { Action, Detail, EmailPreview, Filter, QueueItem } from '../domain/approvals';

export const fetchQueue = (filter: Filter) => getJson<QueueItem[]>(`/v1/organisations/?filter=${filter}`);

export const fetchDetail = (tenantId: string) => getJson<Detail>(`/v1/organisations/${tenantId}`);

export async function decide(tenantId: string, action: Action, reason: string): Promise<EmailPreview> {
  return (await post(`/v1/organisations/${tenantId}/${action}`, { reason })).json() as Promise<EmailPreview>;
}

/** The document as bytes for the viewer, held in memory and shown from a blob URL. Nothing is saved or offered for download. */
export async function fetchDocument(tenantId: string, documentId: string): Promise<{ url: string; contentType: string }> {
  const res = await fetch(`/v1/organisations/${tenantId}/documents/${documentId}`, { credentials: 'same-origin', cache: 'no-store' });
  if (!res.ok) throw new ApiError(res.status);
  const blob = await res.blob();
  return { url: URL.createObjectURL(blob), contentType: blob.type };
}

export const release = (url: string) => URL.revokeObjectURL(url);
