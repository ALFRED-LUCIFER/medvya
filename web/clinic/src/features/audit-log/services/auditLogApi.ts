import { apiFetch } from '../../api/services/apiClient';
import { ApiError, csrf, send } from '../../schedule/services/scheduleApi';
import { exportBody, listQuery, type AuditFilters, type AuditPage } from '../domain/auditLog';

// Practice access log (F-12, MDV-P1-035). The patient is an opaque id in the path; no names or search text travel in URLs.
export const fetchAuditPage = (f: AuditFilters, after: string | null) => {
  const q = listQuery(f, after);
  return send<AuditPage>(`/v1/audit/access-log${f.patientId ? `/patients/${encodeURIComponent(f.patientId)}` : ''}${q ? `?${q}` : ''}`);
};

/** Downloads the watermarked CSV. POST, so the export is its own audited action. */
export async function downloadAuditCsv(f: AuditFilters): Promise<void> {
  const res = await apiFetch('/v1/audit/access-log/export', {
    method: 'POST', credentials: 'same-origin', headers: { ...csrf, 'Content-Type': 'application/json' }, body: JSON.stringify(exportBody(f)),
  });
  if (!res.ok) throw new ApiError(res.status);
  const url = URL.createObjectURL(await res.blob());
  const link = document.createElement('a');
  link.href = url;
  link.download = 'access-log.csv';
  link.click();
  URL.revokeObjectURL(url);
}
