import { send } from '../../api/services/apiClient';
import type { HistoryEntry, HistorySection } from '../domain/chart';

// History sections (MDV-P2-032). The patient id and the section name are the only things in the path; the text is in the body.
export const saveHistory = (patientId: string, section: HistorySection, text: string) =>
  send<{ items: HistoryEntry[] }>(`/v1/patients/${patientId}/history/${section}`, {
    method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ text }),
  });
