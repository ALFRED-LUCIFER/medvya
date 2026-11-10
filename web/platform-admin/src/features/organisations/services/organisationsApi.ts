import { getJson, post } from '../../api/services/api';
import type { Decision, OrganisationDetail, QueueFilter, QueueItem } from '../domain/organisations';

export const fetchQueue = (filter: QueueFilter) => getJson<QueueItem[]>(`/v1/organisations?filter=${filter}`);

export const fetchDetail = (tenantId: string) => getJson<OrganisationDetail>(`/v1/organisations/${encodeURIComponent(tenantId)}`);

export const decide = async (tenantId: string, decision: Decision, reason: string) => {
  await post(`/v1/organisations/${encodeURIComponent(tenantId)}/${decision}`, { reason });
};

/** A five-minute read link; asking for it is audited in the organisation. */
export const documentLink = (tenantId: string, documentId: string) =>
  getJson<{ url: string; expiresAt: string }>(`/v1/organisations/${encodeURIComponent(tenantId)}/documents/${encodeURIComponent(documentId)}`);

/**
 * A tab opened during the click (so no pop-up blocker stops it), pointed at the link once it arrives, closed if it does not.
 * shortcut: the browser's own viewer in a new tab, not the design's watermarked no-download viewer; that viewer is MDV-P5-044.
 */
export function openPendingTab() {
  const tab = window.open('about:blank', '_blank');
  if (tab) tab.opener = null;
  return { go: (url: string) => { if (tab) tab.location.href = url; }, close: () => tab?.close() };
}
