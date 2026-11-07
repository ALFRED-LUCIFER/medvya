import { getJson, post } from '../../api/services/api';
import { pageSize, type AuditEntry } from '../domain/audit';

/** One page, newest first, after the entry `after` (the last one shown) when given. */
export const fetchAudit = (after: AuditEntry | null) =>
  getJson<AuditEntry[]>(`/v1/audit?limit=${pageSize}${after ? `&before=${encodeURIComponent(after.occurredAt)}&beforeId=${after.id}` : ''}`);

/** The whole log as CSV, saved by the browser. The server logs the export, with its reason, before it answers. */
export async function exportAudit(reason: string): Promise<void> {
  const blob = await (await post('/v1/audit/export', { reason })).blob();
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = 'platform-audit-log.csv';
  a.click();
  URL.revokeObjectURL(url);
}
